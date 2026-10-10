import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, linkedSignal, signal, viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, Observable, catchError, finalize, map, switchMap } from 'rxjs';
import { BudgetApi } from '../../core/api/budget-api';
import { BudgetRowDto, BudgetSheetDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { Notifier } from '../../core/errors/notifier';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';
import { applyBudget, budgetAmountValidator, isOverBudget, parseBudgetAmount, sourceLabel, usagePercent } from './budget-rules';

const BUDGET_NOT_FOUND_MESSAGE = '這筆預算已不存在';
const BUDGET_CONFLICT_MESSAGE = '這筆預算已在其他裝置修改';

// 行內編輯的對象：本月覆寫值或預設值
type EditTarget = 'override' | 'default';
interface Editing { categoryId: string; target: EditTarget }

export interface BudgetViewRow {
  row: BudgetRowDto;
  name: string;
  usage: number | null;      // 不截斷；進度條另取 min(100)
  over: boolean;
}

// 浮動支出主分類的月預算：預設值與本月覆寫值、實際支出與使用率。不顯示合計（P4 L plan D9）
@Component({
  selector: 'app-budgets-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MonthNav, MatButtonModule, MatMenuModule, MatProgressBarModule, ReactiveFormsModule],
  templateUrl: './budgets.page.html',
  styleUrl: './budgets.page.scss',
})
export class BudgetsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(BudgetApi);
  private readonly currentBook = inject(CurrentBook);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);

  private readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );
  private readonly reload = signal(0);

  // 載入失敗時 interceptor 已通知，保留上一次的清單
  private readonly sheet = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month(), reload: this.reload() }))).pipe(
      switchMap(({ bookId, month }) => this.api.get(bookId, month).pipe(catchError(() => EMPTY))),
    ),
  );
  // PUT 成功時只換掉該列（D5）；重新載入或換月時回到伺服器的資料
  private readonly rows = linkedSignal(() => this.sheet()?.rows ?? []);
  // 同一時間只有一列在編輯；重新載入或換月時取消。換月不等新的表回來就取消，否則 Enter 會把上個月的預填值寫進新月份
  protected readonly editing = linkedSignal<{ sheet: BudgetSheetDto | undefined; month: number }, Editing | null>({
    source: computed(() => ({ sheet: this.sheet(), month: this.month() })), computation: () => null,
  });
  protected readonly saving = signal(false);
  protected readonly amount = new FormControl('', { nonNullable: true, validators: budgetAmountValidator });
  // 從 ⋮ 選單選了之後，文字框出現時立刻取得焦點，Enter／Esc 才有作用（手動驗證發現焦點停在 ⋮）
  private readonly amountInput = viewChild<ElementRef<HTMLInputElement>>('amountInput');
  private readonly focusAmountInput = effect(() => this.amountInput()?.nativeElement.focus());

  protected readonly viewRows = computed<BudgetViewRow[]>(() => {
    const categories = this.currentBook.book()?.categories ?? [];
    return this.rows().map(row => {
      const category = categories.find(c => c.id === row.categoryId);
      const name = !category ? '（未知分類）' : category.archivedAt ? `${category.name}（已封存）` : category.name;
      return { row, name, usage: usagePercent(row), over: isOverBudget(row) };
    });
  });

  protected readonly formatAmount = formatAmount;
  protected readonly sourceLabel = sourceLabel;
  protected readonly min = Math.min;

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }

  protected isEditing(row: BudgetRowDto): boolean {
    return this.editing()?.categoryId === row.categoryId;
  }

  // 預填目前的值：本月是覆寫值，預設是 defaultAmount（D8）；沒有時空白
  protected startEdit(row: BudgetRowDto, target: EditTarget): void {
    const current = target === 'override' ? (row.source === 'Override' ? row.budget : null) : row.defaultAmount;
    this.amount.reset(current === null ? '' : String(current));
    this.editing.set({ categoryId: row.categoryId, target });
  }

  protected cancelEdit(): void {
    this.editing.set(null);
  }

  protected save(): void {
    const editing = this.editing();
    const amount = parseBudgetAmount(this.amount.value);
    if (!editing || amount === null || this.saving()) {
      return;
    }
    const month = this.month();
    const request = editing.target === 'override'
      ? this.api.setOverride(this.bookId(), editing.categoryId, month, amount)
      : this.api.setDefault(this.bookId(), editing.categoryId, amount);
    this.saving.set(true);
    this.run(request.pipe(finalize(() => this.saving.set(false))), budget => {
      // 請求進行中換了月份：畫面上已是別月的列，回傳值不能套用，改為重新載入
      if (this.month() !== month) {
        this.reloadSheet();
        return;
      }
      this.rows.update(rows => rows.map(r => (r.categoryId === budget.categoryId ? applyBudget(r, budget, month) : r)));
      this.editing.set(null);
    });
  }

  // DELETE 回 204 沒有 body，重新載入整張表
  protected clearOverride(row: BudgetRowDto): void {
    this.run(this.api.removeOverride(this.bookId(), row.categoryId, this.month()), () => this.reloadSheet());
  }

  protected clearDefault(row: BudgetRowDto): void {
    this.run(this.api.removeDefault(this.bookId(), row.categoryId), () => this.reloadSheet());
  }

  private reloadSheet(): void {
    this.reload.update(count => count + 1);
  }

  // 所有寫入的共同出口。離線、XSRF 等通用錯誤已由 interceptor 顯示。
  // 預算不做樂觀並行（Q3），但同時寫入仍可能衝突：兩個裝置同時清掉最後一個值時，第二個 DELETE 影響 0 列而回 409
  private run<T>(request: Observable<T>, success: (result: T) => void): void {
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: success,
      error: (error: ApiError) => {
        switch (error.kind) {
          case 'domain':
            this.notifier.show(error.message);
            break;
          case 'validation':
            this.notifier.show(Object.values(error.fieldErrors)[0]?.[0] ?? '輸入的資料不正確');
            break;
          case 'conflict':
            this.notifier.show(BUDGET_CONFLICT_MESSAGE);
            this.reloadSheet();
            break;
          case 'notFound':
            this.notifier.show(BUDGET_NOT_FOUND_MESSAGE);
            this.reloadSheet();
            break;
        }
      },
    });
  }
}
