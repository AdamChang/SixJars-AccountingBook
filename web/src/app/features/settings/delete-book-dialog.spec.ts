import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { BookDeletionPreviewDto } from '../../core/api/dto';
import { DeleteBookDialog } from './delete-book-dialog';

const PREVIEW: BookDeletionPreviewDto = {
  name: '我的帳本', openingDate: '2026-01-01', lockDate: '2026-03-31', transactions: 12, plannedExpenses: 3,
  recurringPlannedExpenses: 2, categoryBudgets: 4, members: 1, auditEntries: 40,
};

async function open() {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: MAT_DIALOG_DATA, useValue: { bookId: 'book-1', name: '我的帳本' } },
      { provide: MatDialogRef, useValue: { close } },
    ],
  });
  const fixture = TestBed.createComponent(DeleteBookDialog);
  const httpTesting = TestBed.inject(HttpTestingController);
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  const flushPreview = async () => {
    httpTesting.expectOne('/api/books/book-1/deletion-preview').flush(PREVIEW);
    await fixture.whenStable();
  };
  const deleteButton = () => loader.getHarness(MatButtonHarness.with({ text: '永久刪除' }));
  return { fixture, httpTesting, loader, el, close, flushPreview, deleteButton };
}

describe('DeleteBookDialog', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows_counts_lock_date_and_backup_link_after_preview_loads', async () => {
    const { el, flushPreview, fixture } = await open();
    await fixture.whenStable();
    expect(el.textContent).toContain('載入中');

    await flushPreview();
    const text = el.textContent!;
    expect(text).toContain('12 筆');
    expect(text).toContain('2026-03-31');
    expect(text).toContain('40 筆');
    expect(el.querySelector<HTMLAnchorElement>('a.backup')!.getAttribute('href')).toBe('/api/books/book-1/export/backup.json');
  });

  it('delete_is_enabled_only_when_name_matches_exactly', async () => {
    const { loader, flushPreview, deleteButton, close } = await open();
    await flushPreview();
    const input = await loader.getHarness(MatInputHarness.with({ selector: '.confirm-name input' }));
    expect(await (await deleteButton()).isDisabled()).toBe(true);

    await input.setValue('我的帳本 ');
    expect(await (await deleteButton()).isDisabled()).toBe(true);

    await input.setValue('我的帳本');
    await (await deleteButton()).click();
    expect(close).toHaveBeenCalledWith('我的帳本');
  });

  it('failed_preview_keeps_delete_disabled', async () => {
    const { httpTesting, fixture, el, deleteButton } = await open();
    httpTesting.expectOne('/api/books/book-1/deletion-preview').flush(null, { status: 503, statusText: '' });
    await fixture.whenStable();

    expect(el.textContent).toContain('無法載入');
    expect(await (await deleteButton()).isDisabled()).toBe(true);
  });
});
