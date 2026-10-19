import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, catchError, filter, map, switchMap } from 'rxjs';
import { BookDto, TransactionDto } from '../../core/api/dto';
import { TransactionApi } from '../../core/api/transaction-api';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { CONFLICT_MESSAGE, LOCKED_MESSAGE, NOT_FOUND_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { confirm } from '../../shared/confirm-dialog';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';
import { editTransaction } from './edit-transaction-dialog';
import { TransactionForm } from './transaction-form';
import { TransactionList } from './transaction-list';
import { KIND_RULES } from './transaction-rules';

@Component({
  selector: 'app-transactions-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatSelectModule, MonthNav, TransactionForm, TransactionList],
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

  private readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份，不讓頁面壞掉
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );
  // 標籤篩選只篩當月，換月份時保留（F8）；query param 不存在時不篩選
  protected readonly tagId = toSignal(this.route.queryParamMap.pipe(map(params => params.get('tag'))), { requireSync: true });
  // 含已封存的標籤，才篩得到舊交易
  protected readonly tagOptions = computed(() =>
    [...(this.currentBook.book()?.tags ?? [])].sort((a, b) => a.sortOrder - b.sortOrder));
  protected readonly filterTag = computed(() => this.tagOptions().find(tag => tag.id === this.tagId()) ?? null);
  private readonly reload = signal(0);

  // 表單只在初始化／切換類型時套用規則，換帳本必須重建；用 @for 以 book.id 為 key 達成
  protected readonly currentBookAsList = computed(() => {
    const book = this.currentBook.book();
    return book ? [book] : [];
  });

  // 載入失敗時 interceptor 已通知，保留上一次的清單
  protected readonly transactions = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month(), tagId: this.tagId(), reload: this.reload() }))).pipe(
      switchMap(({ bookId, month, tagId }) => this.transactionApi.list(bookId, month, tagId).pipe(catchError(() => EMPTY))),
    ),
    { initialValue: [] as TransactionDto[] },
  );

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }

  // null 時移除 tag query param
  protected changeTag(tag: string | null): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { tag }, queryParamsHandling: 'merge' });
  }

  // 頂端表單只負責新增
  protected onSaved(): void {
    this.reloadList();
  }

  // 換帳本或月份時 MatDialog 預設隨導覽關閉
  protected edit(book: BookDto, transaction: TransactionDto): void {
    editTransaction(this.dialog, { book, transaction })
      .pipe(filter(result => result !== undefined), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reloadList());
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
        this.reloadList();
        break;
      case 'domain':
        this.notifier.show(error.code === 'locked' ? LOCKED_MESSAGE : error.message);
        break;
    }
  }

  private reloadList(): void {
    this.reload.update(count => count + 1);
  }
}
