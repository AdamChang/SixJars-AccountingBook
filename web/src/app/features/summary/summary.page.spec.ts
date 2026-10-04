import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { LedgerSummaryDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { BOOK } from '../transactions/testing/book-fixture';
import { SummaryPage } from './summary.page';

const SUMMARY: LedgerSummaryDto = {
  budgetMonth: 202603, asOf: '2026-03-31', monthlyDisposable: 12345.5, yearToDate: -2000, availableCash: 50000,
  availableCashWithEWallets: 50500,
  accounts: [
    { id: 'acc-loan', name: '房貸', balance: -800000 },
    { id: 'acc-ewallet', name: '悠遊卡', balance: 500 },
    { id: 'acc-card', name: '信用卡', balance: -3200 },
    { id: 'acc-bank', name: '銀行', balance: 49000 },
    { id: 'acc-cash', name: '現金', balance: 1000 },
  ],
  planningFunds: [{ id: 'fund-travel', name: '旅遊基金', balance: 8000 }],
};
const url = (month: number) => `/api/books/${BOOK.id}/summary?budgetMonth=${month}`;

async function setup(path: string, summary: LedgerSummaryDto = SUMMARY, month = 202603) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId', children: [{ path: 'summary', component: SummaryPage }] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: CurrentBook, useValue: { book: signal(BOOK) } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(path, SummaryPage);
  const httpTesting = TestBed.inject(HttpTestingController);
  httpTesting.expectOne(url(month)).flush(summary);
  await harness.fixture.whenStable();
  const el = harness.fixture.nativeElement as HTMLElement;
  return { el, httpTesting };
}

describe('SummaryPage', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.useRealTimers();
  });

  it('loads_summary_for_query_month', async () => {
    const { el } = await setup('/books/book-1/summary?month=202603');
    expect(el.textContent).toContain('2026/03');
  });

  it('shows_three_cards_with_formatted_amounts', async () => {
    const { el } = await setup('/books/book-1/summary?month=202603');
    const cards = [...el.querySelectorAll('mat-card.metric')].map(card => card.textContent ?? '');
    expect(cards).toHaveLength(3);
    expect(cards[0]).toContain('月可用餘額');
    expect(cards[0]).toContain('12,345.5');
    expect(cards[1]).toContain('年累計餘額');
    expect(cards[1]).toContain('-2,000');
    expect(cards[2]).toContain('可用現金');
    expect(cards[2]).toContain('50,000');
    expect(cards[2]).toContain('含電子錢包 50,500');
  });

  it('groups_accounts_by_type_in_fixed_order', async () => {
    const { el } = await setup('/books/book-1/summary?month=202603');
    const titles = [...el.querySelectorAll('.account-group h3')].map(h => h.textContent?.trim());
    expect(titles).toEqual(['現金', '銀行', '電子錢包', '信用卡', '貸款']);
    const credit = el.querySelectorAll('.account-group')[3].textContent ?? '';
    expect(credit).toContain('信用卡');
    expect(credit).toContain('-3,200');
  });

  it('puts_unknown_accounts_in_other_group', async () => {
    const { el } = await setup('/books/book-1/summary?month=202603', {
      ...SUMMARY, accounts: [{ id: 'acc-gone', name: '已刪帳戶', balance: 7 }, ...SUMMARY.accounts],
    });
    const groups = [...el.querySelectorAll('.account-group')];
    const titles = groups.map(g => g.querySelector('h3')?.textContent?.trim());
    expect(titles.at(-1)).toBe('其他');
    expect(groups.at(-1)!.textContent).toContain('已刪帳戶');
  });

  it('lists_planning_funds', async () => {
    const { el } = await setup('/books/book-1/summary?month=202603');
    const section = el.querySelector('.planning-funds')!;
    expect(section.textContent).toContain('財務規劃帳戶');
    expect(section.textContent).toContain('旅遊基金');
    expect(section.textContent).toContain('8,000');
  });
});
