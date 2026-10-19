import { TestBed } from '@angular/core/testing';
import { TagDto } from '../core/api/dto';
import { TagChip } from './tag-chip';

describe('TagChip', () => {
  async function render(tag: TagDto) {
    const fixture = TestBed.createComponent(TagChip);
    fixture.componentRef.setInput('tag', tag);
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).querySelector('.tag-chip') as HTMLElement;
  }

  it('shows_name_with_palette_tokens', async () => {
    const chip = await render({ id: 't1', name: '旅遊', color: 'Teal', sortOrder: 0, archivedAt: null });
    expect(chip.textContent?.trim()).toBe('旅遊');
    expect(chip.style.getPropertyValue('background-color')).toBe('var(--tag-teal-bg)');
    expect(chip.style.getPropertyValue('color')).toBe('var(--tag-teal-fg)');
    expect(chip.classList.contains('archived')).toBe(false);
    expect(chip.getAttribute('title')).toBeNull();
  });

  it('marks_archived_tag', async () => {
    const chip = await render({ id: 't1', name: '舊專案', color: 'Purple', sortOrder: 0, archivedAt: '2026-10-01T00:00:00Z' });
    expect(chip.classList.contains('archived')).toBe(true);
    expect(chip.getAttribute('title')).toBe('（已封存）');
  });
});
