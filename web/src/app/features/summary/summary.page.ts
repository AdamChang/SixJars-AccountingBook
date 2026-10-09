import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EMPTY, catchError, map, switchMap } from 'rxjs';
import { BudgetApi } from '../../core/api/budget-api';
import { AccountType, BalanceDto } from '../../core/api/dto';
import { SummaryApi } from '../../core/api/summary-api';
import { CurrentBook } from '../../core/book/current-book';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';

interface AccountGroup { title: string; accounts: BalanceDto[] }

// 組的順序即畫面順序
const GROUPS: { type: AccountType; title: string }[] = [
  { type: 'Cash', title: '現金' },
  { type: 'Bank', title: '銀行' },
  { type: 'EWallet', title: '電子錢包' },
  { type: 'CreditCard', title: '信用卡' },
  { type: 'Loan', title: '貸款' },
];

@Component({
  selector: 'app-summary-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MonthNav, RouterLink],
  templateUrl: './summary.page.html',
  styleUrl: './summary.page.scss',
})
export class SummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly summaryApi = inject(SummaryApi);
  private readonly budgetApi = inject(BudgetApi);
  private readonly currentBook = inject(CurrentBook);

  protected readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );

  // 載入失敗時 interceptor 已通知，保留上一次的數字
  protected readonly summary = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month() }))).pipe(
      switchMap(({ bookId, month }) => this.summaryApi.get(bookId, month).pipe(catchError(() => EMPTY))),
    ),
  );

  // 本月浮動預算另外呼叫 /budgets，不擴充 /summary（spec §5.3）；直接顯示後端 totals（P4 L plan D6）。
  // 載入失敗時 interceptor 已通知，不顯示卡片
  protected readonly budgets = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month() }))).pipe(
      switchMap(({ bookId, month }) => this.budgetApi.get(bookId, month).pipe(catchError(() => EMPTY))),
    ),
  );
  protected readonly hasBudget = computed(() => this.budgets()?.rows.some(row => row.budget !== null) ?? false);

  // 餘額照後端數字顯示不改正負號；帳本裡找不到的帳戶放「其他」
  protected readonly groups = computed<AccountGroup[]>(() => {
    const summary = this.summary();
    if (!summary) {
      return [];
    }
    const typeById = new Map(this.currentBook.book()?.accounts.map(account => [account.id, account.type]));
    const groups: AccountGroup[] = GROUPS.map(({ type, title }) => ({
      title,
      accounts: summary.accounts.filter(account => typeById.get(account.id) === type),
    })).filter(group => group.accounts.length > 0);
    const others = summary.accounts.filter(account => !typeById.has(account.id));
    return others.length > 0 ? [...groups, { title: '其他', accounts: others }] : groups;
  });

  protected readonly format = formatAmount;

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }
}
