import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatDialogHarness } from '@angular/material/dialog/testing';
import { confirm } from './confirm-dialog';

@Component({ template: '' })
class Host {}

describe('confirm', () => {
  async function run(action: '刪除' | '取消' | 'dismiss') {
    const fixture = TestBed.createComponent(Host);
    const results: boolean[] = [];
    confirm(TestBed.inject(MatDialog), '確定要刪除這筆交易？').subscribe(value => results.push(value));
    const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const dialog = await loader.getHarness(MatDialogHarness);
    expect(await dialog.getText()).toContain('確定要刪除這筆交易？');
    if (action === 'dismiss') {
      await dialog.close();
    } else {
      await (await loader.getHarness(MatButtonHarness.with({ text: action }))).click();
    }
    // 關閉動畫結束後 afterClosed 才會發出
    await vi.waitFor(() => expect(results).toHaveLength(1));
    return results;
  }

  it('returns_true_only_when_confirmed', async () => {
    expect(await run('刪除')).toEqual([true]);
  });

  it('returns_false_when_cancelled', async () => {
    expect(await run('取消')).toEqual([false]);
  });

  it('returns_false_when_dismissed', async () => {
    expect(await run('dismiss')).toEqual([false]);
  });
});
