import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, linkedSignal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, filter, map, switchMap } from 'rxjs';
import { BookApi } from '../../core/api/book-api';
import { SessionService } from '../../core/auth/session.service';
import { CurrentBook } from '../../core/book/current-book';
import { ApiError } from '../../core/errors/api-error';
import { Notifier } from '../../core/errors/notifier';
import { confirm } from '../../shared/confirm-dialog';
import { nextTagColor } from '../../shared/tags';
import { DeleteBookDialog, DeleteBookDialogData } from './delete-book-dialog';
import { SettingDialog, SettingDialogData, SettingDialogResult } from './setting-dialog';
import { ReorderRequest, SettingList } from './setting-list';
import { SettingGroup, accountGroup, categoryGroups, fundGroup, reorderRequest, tagGroup } from './settings-rules';

// ?tab= 的值，依分頁順序（spec F5）；不認得的值用第一個分頁
const TAB_KEYS = ['categories', 'accounts', 'funds', 'tags', 'data'];

const NOT_FOUND = '此項目已不存在';

const labelOf = (group: SettingGroup, id: string) => group.rows.find(row => row.id === id)?.label ?? '';

// 帳本設定：分類、帳戶、財務規劃帳戶、標籤的維護，加上鎖帳日與下載
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
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  // bookGuard 已經載入帳本
  protected readonly book = computed(() => this.currentBook.book()!);
  protected readonly categoryGroups = computed(() => categoryGroups(this.book().categories));
  protected readonly accounts = computed(() => accountGroup(this.book().accounts));
  protected readonly funds = computed(() => fundGroup(this.book().planningFunds));
  protected readonly tags = computed(() => tagGroup(this.book().tags));
  protected readonly lockDate = linkedSignal(() => this.book().lockDate ?? '');
  private readonly tabParam = toSignal(inject(ActivatedRoute).queryParamMap.pipe(map(params => params.get('tab'))), {
    requireSync: true,
  });
  // 只讀不寫：切換分頁不改網址（spec F5）。路由會重用這個元件（在設定頁建立另一本帳本），所以換帳本時重新套用 ?tab=；
  // 但寫入後 currentBook.reload 會換掉 book 物件，同一本帳本不能因此跳回 ?tab= 的分頁，所以只比較 id 與 tab
  private readonly tabTarget = computed(
    () => ({ bookId: this.book().id, tab: this.tabParam() }),
    { equal: (a, b) => a.bookId === b.bookId && a.tab === b.tab },
  );
  protected readonly selectedTab = linkedSignal({
    source: this.tabTarget,
    computation: ({ tab }) => Math.max(0, TAB_KEYS.indexOf(tab ?? '')),
  });

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
      defaultColor: group.path === 'tags' ? nextTagColor(this.book().tags.map(t => t.color)) : undefined,
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
        case 'tags':
          return this.run(this.api.add(bookId, 'tags', { name: result.name, color: result.color! }), added);
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
      case 'tags': {
        const tag = book.tags.find(t => t.id === id)!;
        this.openDialog({ mode: 'edit', path: 'tags', name: tag.name, color: tag.color },
          result => this.run(this.api.updateTag(book.id, id, { name: result.name, color: result.color! }), updated(result)));
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

  protected onDeleteBook(): void {
    const book = this.book();
    // 只剩這一本、而且不是建立者時，才警告刪除後無法再登入（#8）；建立者沒有帳本也能登入（ADR 0011）
    const me = this.session.me();
    const isLastBook = me?.books.length === 1 && !me.canCreateBooks;
    this.dialog.open<DeleteBookDialog, DeleteBookDialogData, string>(DeleteBookDialog, { data: { bookId: book.id, name: book.name, isLastBook } })
      .afterClosed()
      .pipe(
        filter((name): name is string => name !== undefined),
        switchMap(name => this.api.delete(book.id, name)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.leaveDeletedBook(`已刪除帳本「${book.name}」`),
        error: (error: ApiError) => {
          switch (error.kind) {
            case 'notFound':
              // 帳本已經不存在（例如在其他分頁刪掉了），結果與刪除成功相同
              void this.leaveDeletedBook(null);
              break;
            case 'domain':
              this.notifier.show(error.message);
              break;
          }
        },
      });
  }

  // spec F3：先重新載入 me，首頁才不會列出（或自動導回）已刪除的帳本；
  // clear 放在導頁之後，否則這一頁的 book()! 會在離開前拿到 undefined
  private async leaveDeletedBook(message: string | null): Promise<void> {
    await this.session.reload();
    await this.router.navigate(['/']);
    this.currentBook.clear();
    if (message) {
      this.notifier.show(message);
    }
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
