import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { BookDto, TransactionDto } from '../../core/api/dto';
import { formatAmount } from '../../shared/money';
import { KIND_RULES, categoryLabel, isLocked } from './transaction-rules';

interface TransactionRow {
  dto: TransactionDto;
  date: string;
  kindLabel: string;
  accountText: string;
  categoryText: string;
  amountText: string;
  note: string;
  negative: boolean;
  locked: boolean;
}

@Component({
  selector: 'app-transaction-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTableModule],
  templateUrl: './transaction-list.html',
  styleUrl: './transaction-list.scss',
})
export class TransactionList {
  readonly transactions = input.required<TransactionDto[]>();
  readonly book = input.required<BookDto>();
  readonly editingId = input<string | null>(null);
  readonly edit = output<TransactionDto>();
  readonly remove = output<TransactionDto>();

  protected readonly columns = ['date', 'kind', 'account', 'category', 'amount', 'note', 'actions'];

  protected readonly rows = computed<TransactionRow[]>(() => {
    const book = this.book();
    const accountName = (id: string | null) => book.accounts.find(account => account.id === id)?.name ?? '';
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
        negative: dto.amount < 0,
        locked: isLocked(dto.date, book.lockDate),
      };
    });
  });

  protected onRowClick(row: TransactionRow): void {
    if (!row.locked) {
      this.edit.emit(row.dto);
    }
  }

  protected onDelete(event: Event, row: TransactionRow): void {
    // 刪除按鈕在列內，不能同時觸發列的編輯
    event.stopPropagation();
    this.remove.emit(row.dto);
  }
}
