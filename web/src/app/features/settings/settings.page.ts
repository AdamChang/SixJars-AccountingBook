import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, linkedSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { Observable, filter } from 'rxjs';
import { BookApi } from '../../core/api/book-api';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { Notifier } from '../../core/errors/notifier';
import { confirm } from '../../shared/confirm-dialog';
import { SettingDialog, SettingDialogData, SettingDialogResult } from './setting-dialog';
import { ReorderRequest, SettingList } from './setting-list';
import { SettingGroup, accountGroup, categoryGroups, fundGroup, reorderRequest } from './settings-rules';

const NOT_FOUND = '此項目已不存在';

const labelOf = (group: SettingGroup, id: string) => group.rows.find(row => row.id === id)?.label ?? '';

// 帳本設定：分類、帳戶、財務規劃帳戶的維護，加上鎖帳日與下載
@Component({
  selector: 'app-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, MatTabsModule, MatButtonModule, MatFormFieldModule, MatInputModule, SettingList],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss',
})
export class SettingsPage {
  private readonly currentBook = inject(CurrentBook);
  private readonly api = inject(BookApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);

  // bookGuard 已經載入帳本
  protected readonly book = computed(() => this.currentBook.book()!);
  protected readonly categoryGroups = computed(() => categoryGroups(this.book().categories));
  protected readonly accounts = computed(() => accountGroup(this.book().accounts));
  protected readonly funds = computed(() => fundGroup(this.book().planningFunds));
  protected readonly lockDate = linkedSignal(() => this.book().lockDate ?? '');

  protected exportUrl(file: string): string {
    return `/api/books/${this.book().id}/export/${file}`;
  }

  // 排序成功不顯示訊息；失敗時把畫面還原成送出前的順序
  protected onReorder(group: SettingGroup, request: ReorderRequest): void {
    this.run(this.api.reorder(this.book().id, group.path, reorderRequest(group, request.ids)), null, () => request.revert());
  }

  protected onArchive(group: SettingGroup, id: string): void {
    this.run(this.api.archive(this.book().id, group.path, id), `已封存「${labelOf(group, id)}」`);
  }

  protected onUnarchive(group: SettingGroup, id: string): void {
    this.run(this.api.unarchive(this.book().id, group.path, id), `已解除封存「${labelOf(group, id)}」`);
  }

  protected onRemove(group: SettingGroup, id: string): void {
    const label = labelOf(group, id);
    confirm(this.dialog, `確定要刪除「${label}」？`)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.run(this.api.remove(this.book().id, group.path, id), `已刪除「${label}」`));
  }

  protected onAdd(group: SettingGroup): void {
    const bookId = this.book().id;
    const data: SettingDialogData = {
      mode: 'add', path: group.path, categoryKind: group.categoryKind ?? undefined, isMain: group.parentId === null,
    };
    this.openDialog(data, result => {
      const added = `已新增「${result.name}」`;
      switch (group.path) {
        case 'accounts':
          return this.run(this.api.add(bookId, 'accounts', {
            name: result.name, type: result.type!, openingBalance: result.openingBalance!,
            countsAsAvailableCash: result.countsAsAvailableCash!,
          }), added);
        case 'planning-funds':
          return this.run(this.api.add(bookId, 'planning-funds', { name: result.name, openingBalance: result.openingBalance! }), added);
        case 'categories':
          return this.run(this.api.add(bookId, 'categories', {
            name: result.name, kind: group.categoryKind!, nature: result.nature, parentId: group.parentId,
          }), added);
      }
    });
  }

  protected onEdit(group: SettingGroup, id: string): void {
    const book = this.book();
    const updated = (result: SettingDialogResult) => `已更新「${result.name}」`;
    switch (group.path) {
      case 'accounts': {
        const account = book.accounts.find(a => a.id === id)!;
        this.openDialog(
          { mode: 'edit', path: 'accounts', name: account.name, accountType: account.type, countsAsAvailableCash: account.countsAsAvailableCash },
          result => this.run(this.api.updateAccount(book.id, id, {
            name: result.name, countsAsAvailableCash: result.countsAsAvailableCash ?? false,
          }), updated(result)));
        break;
      }
      case 'planning-funds': {
        const fund = book.planningFunds.find(f => f.id === id)!;
        this.openDialog({ mode: 'edit', path: 'planning-funds', name: fund.name },
          result => this.run(this.api.updatePlanningFund(book.id, id, { name: result.name }), updated(result)));
        break;
      }
      case 'categories': {
        const category = book.categories.find(c => c.id === id)!;
        this.openDialog(
          {
            mode: 'edit', path: 'categories', name: category.name, categoryKind: category.kind,
            isMain: category.parentId === null, nature: category.nature,
          },
          // nature 為 null（非支出主分類）時後端不修改性質
          result => this.run(this.api.updateCategory(book.id, id, { name: result.name, nature: result.nature }), updated(result)));
        break;
      }
    }
  }

  protected saveLockDate(): void {
    this.run(this.api.setLockDate(this.book().id, this.lockDate() || null), '已更新鎖帳日');
  }

  protected clearLockDate(): void {
    this.lockDate.set('');
    this.saveLockDate();
  }

  private openDialog(data: SettingDialogData, save: (result: SettingDialogResult) => void): void {
    this.dialog.open<SettingDialog, SettingDialogData, SettingDialogResult>(SettingDialog, { data })
      .afterClosed()
      .pipe(filter((result): result is SettingDialogResult => result !== undefined), takeUntilDestroyed(this.destroyRef))
      .subscribe(save);
  }

  // 所有寫入的共同出口：成功後重新載入帳本，讓設定頁與記帳頁的選項同步。
  // 離線、XSRF 等通用錯誤已由 interceptor 顯示，這裡只處理與這個動作有關的錯誤。
  private run(request: Observable<unknown>, success: string | null, onError?: () => void): void {
    const bookId = this.book().id;
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (success) {
          this.notifier.show(success);
        }
        void this.currentBook.reload(bookId);
      },
      error: (error: ApiError) => {
        onError?.();
        switch (error.kind) {
          case 'domain':
            // 被參照、餘額不為 0 等都是 422 + code，訊息由後端提供（P4 J plan D2）
            this.notifier.show(error.message);
            break;
          case 'validation':
            this.notifier.show(Object.values(error.fieldErrors)[0]?.[0] ?? '輸入的資料不正確');
            break;
          case 'notFound':
            this.notifier.show(NOT_FOUND);
            void this.currentBook.reload(bookId);
            break;
        }
      },
    });
  }
}
