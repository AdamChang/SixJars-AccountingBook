import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, Observable, catchError, filter, finalize, map, switchMap } from 'rxjs';
import {
  PlannedExpenseDto, PlannedExpenseInput, RecurringPlannedExpenseDto, RecurringPlannedExpenseInput,
} from '../../core/api/dto';
import { PlannedExpenseApi } from '../../core/api/planned-expense-api';
import { RecurringPlannedExpenseApi } from '../../core/api/recurring-planned-expense-api';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { PLANNED_CONFLICT_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { confirm } from '../../shared/confirm-dialog';
import { budgetMonthOf, parseBudgetMonth, toDateString } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';
import { PayDialog, PayDialogData, PayDialogResult } from './pay-dialog';
import { PlannedExpenseDialog, PlannedExpenseDialogData } from './planned-expense-dialog';
import {
  PlannedRow, categoryLabel, describeRecurrence, generationMessage, plannedGroups, refreshMessage,
} from './planned-expense-rules';
import { RecurringDialog, RecurringDialogData } from './recurring-dialog';

const LOCKED_MONTH_MESSAGE = '這個月份已鎖帳，不能異動';
const PLANNED_NOT_FOUND_MESSAGE = '這筆預定支出已不存在';
const RECURRING_CONFLICT_MESSAGE = '這個週期項目已在其他裝置修改';
const RECURRING_NOT_FOUND_MESSAGE = '這個週期項目已不存在';
const RECURRING_TAB = 1;

export interface RecurringRow {
  item: RecurringPlannedExpenseDto;
  categoryLabel: string;
  description: string;
  accountName: string | null;
  amount: number;          // 正數，顯示用
  ended: boolean;          // 結束月份早於頁面月份
}

// 寫入後要重新載入哪一份清單，以及失敗時對應的訊息
interface WriteTarget { reload: () => void; conflict: string; notFound: string }

// 預定支出：本月清單（產生、以現值更新、付款、增刪改）與週期項目
@Component({
  selector: 'app-planned-expenses-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MonthNav, MatTabsModule, MatButtonModule, MatMenuModule],
  templateUrl: './planned-expenses.page.html',
  styleUrl: './planned-expenses.page.scss',
})
export class PlannedExpensesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(PlannedExpenseApi);
  private readonly recurringApi = inject(RecurringPlannedExpenseApi);
  private readonly currentBook = inject(CurrentBook);
  private readonly notifier = inject(Notifier);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  // bookGuard 已經載入帳本
  protected readonly book = computed(() => this.currentBook.book()!);
  private readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );
  private readonly reload = signal(0);
  // 產生與以現值更新進行中時停用兩個按鈕（D11：不處理同時產生造成的唯一索引衝突）
  protected readonly pending = signal(false);

  // 載入失敗時 interceptor 已通知，保留上一次的清單
  private readonly planned = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month(), reload: this.reload() }))).pipe(
      switchMap(({ bookId, month }) => this.api.list(bookId, month).pipe(catchError(() => EMPTY))),
    ),
    { initialValue: [] as PlannedExpenseDto[] },
  );
  protected readonly groups = computed(() => plannedGroups(this.planned(), this.book()));

  // 週期項目在切到該分頁時才載入
  protected readonly selectedTab = signal(0);
  private readonly recurringReload = signal(0);
  private readonly recurringItems = toSignal(
    toObservable(computed(() => ({
      bookId: this.bookId(), active: this.selectedTab() === RECURRING_TAB, reload: this.recurringReload(),
    }))).pipe(
      filter(({ active }) => active),
      switchMap(({ bookId }) => this.recurringApi.list(bookId).pipe(catchError(() => EMPTY))),
    ),
    { initialValue: [] as RecurringPlannedExpenseDto[] },
  );
  protected readonly recurringRows = computed<RecurringRow[]>(() => {
    const book = this.book();
    return this.recurringItems().map(item => ({
      item,
      categoryLabel: categoryLabel(book, item.categoryId),
      description: describeRecurrence(item),
      accountName: book.accounts.find(a => a.id === item.accountId)?.name ?? null,
      amount: Math.abs(item.defaultAmount),
      ended: item.endMonth !== null && item.endMonth < this.month(),
    }));
  });

  private readonly plannedTarget: WriteTarget = {
    reload: () => this.reload.update(count => count + 1),
    conflict: PLANNED_CONFLICT_MESSAGE, notFound: PLANNED_NOT_FOUND_MESSAGE,
  };
  private readonly recurringTarget: WriteTarget = {
    reload: () => this.recurringReload.update(count => count + 1),
    conflict: RECURRING_CONFLICT_MESSAGE, notFound: RECURRING_NOT_FOUND_MESSAGE,
  };

  protected readonly formatAmount = formatAmount;

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }

  protected generate(): void {
    this.runPending(this.api.generate(this.bookId(), this.month()).pipe(map(generationMessage)));
  }

  protected refresh(): void {
    this.runPending(this.api.refresh(this.bookId(), this.month()).pipe(map(refreshMessage)));
  }

  protected add(): void {
    this.openPlannedDialog({ book: this.book(), budgetMonth: this.month() },
      input => this.run(this.api.create(this.bookId(), input), '已新增預定支出', this.plannedTarget));
  }

  protected edit(row: PlannedRow): void {
    const planned = row.planned;
    this.openPlannedDialog({ book: this.book(), budgetMonth: this.month(), planned },
      input => this.run(this.api.update(this.bookId(), planned.id, planned.version, input), '已更新預定支出', this.plannedTarget));
  }

  protected pay(row: PlannedRow): void {
    const planned = row.planned;
    const data: PayDialogData = { planned, book: this.book(), today: toDateString(new Date()) };
    this.dialog.open<PayDialog, PayDialogData, PayDialogResult>(PayDialog, { data })
      .afterClosed()
      .pipe(filter((result): result is PayDialogResult => !!result), takeUntilDestroyed(this.destroyRef))
      .subscribe(result =>
        this.run(this.api.pay(this.bookId(), planned.id, { version: planned.version, ...result }), '已付款', this.plannedTarget));
  }

  // 刪除已付款的預定支出不影響付款交易
  protected remove(row: PlannedRow): void {
    const planned = row.planned;
    confirm(this.dialog, `確定要刪除「${row.categoryLabel}」${formatAmount(row.amount)}？`)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.run(this.api.delete(this.bookId(), planned.id, planned.version), '已刪除預定支出', this.plannedTarget));
  }

  protected addRecurring(): void {
    this.openRecurringDialog({ book: this.book(), budgetMonth: this.month() },
      input => this.run(this.recurringApi.create(this.bookId(), input), '已新增週期項目', this.recurringTarget));
  }

  // 修改不回寫已產生的預定支出；要套用到未付的那筆請按「以現值更新」（D5）
  protected editRecurring(row: RecurringRow): void {
    const item = row.item;
    this.openRecurringDialog({ book: this.book(), budgetMonth: this.month(), item },
      input => this.run(
        this.recurringApi.update(this.bookId(), item.id, item.version, input), '已更新週期項目', this.recurringTarget));
  }

  // 已產生過預定支出的項目後端回 422 in-use，訊息由後端提供（D12）
  protected removeRecurring(row: RecurringRow): void {
    const item = row.item;
    confirm(this.dialog, `確定要刪除週期項目「${row.categoryLabel}」？`)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.run(
        this.recurringApi.delete(this.bookId(), item.id, item.version), '已刪除週期項目', this.recurringTarget));
  }

  private openPlannedDialog(data: PlannedExpenseDialogData, save: (input: PlannedExpenseInput) => void): void {
    this.dialog.open<PlannedExpenseDialog, PlannedExpenseDialogData, PlannedExpenseInput>(PlannedExpenseDialog, { data })
      .afterClosed()
      .pipe(filter((input): input is PlannedExpenseInput => !!input), takeUntilDestroyed(this.destroyRef))
      .subscribe(save);
  }

  private openRecurringDialog(data: RecurringDialogData, save: (input: RecurringPlannedExpenseInput) => void): void {
    this.dialog.open<RecurringDialog, RecurringDialogData, RecurringPlannedExpenseInput>(RecurringDialog, { data })
      .afterClosed()
      .pipe(filter((input): input is RecurringPlannedExpenseInput => !!input), takeUntilDestroyed(this.destroyRef))
      .subscribe(save);
  }

  private runPending(request: Observable<string>): void {
    this.pending.set(true);
    this.run(request.pipe(finalize(() => this.pending.set(false))), message => message, this.plannedTarget);
  }

  // 所有寫入的共同出口：成功後重新載入對應的清單。
  // 離線、XSRF 等通用錯誤已由 interceptor 顯示，這裡只處理與這個動作有關的錯誤。
  private run<T>(request: Observable<T>, success: string | ((result: T) => string), target: WriteTarget): void {
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.notifier.show(typeof success === 'string' ? success : success(result));
        target.reload();
      },
      error: (error: ApiError) => {
        switch (error.kind) {
          case 'domain':
            this.notifier.show(error.code === 'locked' ? LOCKED_MONTH_MESSAGE : error.message);
            break;
          case 'validation':
            this.notifier.show(Object.values(error.fieldErrors)[0]?.[0] ?? '輸入的資料不正確');
            break;
          case 'conflict':
            this.notifier.show(target.conflict);
            target.reload();
            break;
          case 'notFound':
            this.notifier.show(target.notFound);
            target.reload();
            break;
        }
      },
    });
  }
}
