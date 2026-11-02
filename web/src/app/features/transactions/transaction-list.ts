import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { BookDto, TagDto, TransactionDto } from '../../core/api/dto';
import { recordedByAgent } from '../../shared/agent-origin';
import { formatAmount } from '../../shared/money';
import { TagChip } from '../../shared/tag-chip';
import { sortTags } from '../../shared/tags';
import { KIND_RULES, NOT_EDITABLE_MESSAGE, categoryLabel, isLocked } from './transaction-rules';

interface TransactionRow {
  dto: TransactionDto;
  date: string;
  kindLabel: string;
  accountText: string;
  categoryText: string;
  amountText: string;
  note: string;
  tags: TagDto[];
  negative: boolean;
  locked: boolean;
  // 不是鎖帳、而是這個類型不能在 Web 編輯時的說明；可編輯時為 null
  notEditableReason: string | null;
  byAgent: boolean;
  createdVia: string | null;
}

@Component({
  selector: 'app-transaction-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTableModule, TagChip],
  templateUrl: './transaction-list.html',
  styleUrl: './transaction-list.scss',
})
export class TransactionList {
  readonly transactions = input.required<TransactionDto[]>();
  readonly book = input.required<BookDto>();
  // 條件已經顯示在選單上，空清單只需要說明是被篩選掉的（transaction filters Q9、F4）
  readonly filtered = input(false);
  readonly edit = output<TransactionDto>();
  readonly remove = output<TransactionDto>();

  protected readonly columns = ['date', 'kind', 'account', 'category', 'amount', 'note', 'actions'];
  protected readonly emptyMessage = computed(() => this.filtered() ? '這個月沒有符合篩選條件的交易' : '這個月還沒有交易');

  protected readonly rows = computed<TransactionRow[]>(() => {
    const book = this.book();
    const accountName = (id: string | null) => book.accounts.find(account => account.id === id)?.name ?? '';
    const fundName = (id: string) => book.planningFunds.find(fund => fund.id === id)?.name ?? '';
    return this.transactions().map(dto => {
      const rule = KIND_RULES[dto.kind];
      const account = accountName(dto.accountId);
      const counter = dto.counterAccountId === null ? null : accountName(dto.counterAccountId);
      // 沒有對方帳戶（例如入新資金沒有轉出）或這個類型沒有方向時，只顯示主帳戶
      let accountText = account;
      if (counter !== null && rule.flow === 'accountToCounter') {
        accountText = `${account} → ${counter}`;
      } else if (counter !== null && rule.flow === 'counterToAccount') {
        accountText = `${counter} → ${account}`;
      } else if (dto.kind === 'BalanceAdjustment' && dto.planningFundId !== null) {
        // 財務規劃帳戶的調整只是掛在現金帳戶、沒有分錄，顯示真正被調整的信封
        accountText = fundName(dto.planningFundId);
      }
      return {
        dto,
        // 直接切字串，不經 Date，避免時區位移
        date: dto.date.slice(5).replace('-', '/'),
        kindLabel: rule.label,
        accountText,
        categoryText: categoryLabel(book.categories, dto.categoryId),
        amountText: formatAmount(dto.amount),
        note: dto.note ?? '',
        // 標籤放在備註欄、備註文字之前，依標籤的設定排序（F9）；封存的也顯示
        tags: sortTags(book.tags, dto.tagIds),
        // 出資金存的是正數，但錢是從財務規劃帳戶流出，比照支出顯示紅字
        negative: dto.amount < 0 || dto.kind === 'FundWithdrawal',
        locked: isLocked(dto.date, book.lockDate),
        notEditableReason: rule.webEditable ? null : NOT_EDITABLE_MESSAGE,
        byAgent: recordedByAgent(dto),
        createdVia: dto.createdVia ?? null,
      };
    });
  });

  protected onRowClick(row: TransactionRow): void {
    if (!row.locked && row.notEditableReason === null) {
      this.edit.emit(row.dto);
    }
  }

  protected onDelete(event: Event, row: TransactionRow): void {
    // 刪除按鈕在列內，不能同時觸發列的編輯
    event.stopPropagation();
    this.remove.emit(row.dto);
  }
}
