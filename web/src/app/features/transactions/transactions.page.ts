import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, linkedSignal, signal, viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, catchError, filter, map, switchMap } from 'rxjs';
import { TransactionDto } from '../../core/api/dto';
import { TransactionApi } from '../../core/api/transaction-api';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { CONFLICT_MESSAGE, LOCKED_MESSAGE, NOT_FOUND_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { confirm } from '../../shared/confirm-dialog';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';
import { TransactionForm } from './transaction-form';
import { TransactionList } from './transaction-list';
import { KIND_RULES } from './transaction-rules';

@Component({
  selector: 'app-transactions-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MonthNav, TransactionForm, TransactionList],
  templateUrl: './transactions.page.html',
  styleUrl: './transactions.page.scss',
})
export class TransactionsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transactionApi = inject(TransactionApi);
  private readonly currentBook = inject(CurrentBook);
  private readonly notifier = inject(Notifier);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formTop = viewChild<ElementRef<HTMLElement>>('formTop');

  private readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份，不讓頁面壞掉
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );
  private readonly reload = signal(0);

  // 表單只在初始化／切換類型時套用規則，換帳本必須重建；用 @for 以 book.id 為 key 達成
  protected readonly currentBookAsList = computed(() => {
    const book = this.currentBook.book();
    return book ? [book] : [];
  });

  // 保存被點的那筆物件本身（不從重新載入的清單查），表單以物件身分判斷是否重新載入，否則每次 reload 都會蓋掉使用者的修改
  // 換帳本時自動結束編輯
  protected readonly editing = linkedSignal<string, TransactionDto | null>({
    source: this.bookId,
    computation: () => null,
  });
  protected readonly editingId = computed(() => this.editing()?.id ?? null);

  // 載入失敗時 interceptor 已通知，保留上一次的清單
  protected readonly transactions = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month(), reload: this.reload() }))).pipe(
      switchMap(({ bookId, month }) => this.transactionApi.list(bookId, month).pipe(catchError(() => EMPTY))),
    ),
    { initialValue: [] as TransactionDto[] },
  );

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.reloadList();
  }

  protected onCancelled(): void {
    this.editing.set(null);
  }

  protected onStale(): void {
    this.editing.set(null);
    this.reloadList();
  }

  protected edit(transaction: TransactionDto): void {
    this.editing.set(transaction);
    // jsdom 沒有實作 scrollIntoView
    this.formTop()?.nativeElement.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
  }

  protected remove(transaction: TransactionDto): void {
    const bookId = this.bookId();
    const message =
      `確定要刪除 ${transaction.date} ${KIND_RULES[transaction.kind].label} ${formatAmount(transaction.amount)}？`;
    confirm(this.dialog, message)
      .pipe(
        filter(confirmed => confirmed),
        switchMap(() => this.transactionApi.delete(bookId, transaction.id, transaction.version)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.notifier.show('已刪除');
          this.stopEditingIfRemoved(transaction);
          this.reloadList();
        },
        error: (error: ApiError) => this.onDeleteFailed(transaction, error),
      });
  }

  // 通用錯誤（離線、XSRF、401）已由 interceptor 處理
  private onDeleteFailed(transaction: TransactionDto, error: ApiError): void {
    switch (error.kind) {
      case 'conflict':
        this.notifier.show(CONFLICT_MESSAGE);
        this.reloadList();
        break;
      case 'notFound':
        this.notifier.show(NOT_FOUND_MESSAGE);
        this.stopEditingIfRemoved(transaction);
        this.reloadList();
        break;
      case 'domain':
        this.notifier.show(error.code === 'locked' ? LOCKED_MESSAGE : error.message);
        break;
    }
  }

  private stopEditingIfRemoved(transaction: TransactionDto): void {
    if (this.editing()?.id === transaction.id) {
      this.editing.set(null);
    }
  }

  private reloadList(): void {
    this.reload.update(count => count + 1);
  }
}
