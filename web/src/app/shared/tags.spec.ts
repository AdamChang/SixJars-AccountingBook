import { TagDto } from '../core/api/dto';
import { TAG_COLORS, TAG_COLOR_LABELS, nextTagColor, sortTags, textLength } from './tags';

const tag = (id: string, sortOrder: number): TagDto => ({ id, name: id, color: 'Red', sortOrder, archivedAt: null });

describe('textLength', () => {
  it.each([
    ['日本旅遊', 4],
    [' ✈️日本 ', 3],
    ['👨‍👩‍👧', 1],
    ['abc', 3],
  ])('counts %s as %s grapheme clusters like the backend StringInfo', (name, expected) => {
    expect(textLength(name)).toBe(expected);
  });
});

describe('nextTagColor', () => {
  it('picks_first_unused_color_or_falls_back_to_first', () => {
    expect(nextTagColor([])).toBe('Red');
    expect(nextTagColor(['Red', 'Orange'])).toBe('Amber');
    expect(nextTagColor(['Orange'])).toBe('Red');
    expect(nextTagColor([...TAG_COLORS])).toBe('Red');
  });
});

describe('sortTags', () => {
  it('returns_tags_of_ids_in_sort_order_skipping_unknown', () => {
    const tags = [tag('b', 1), tag('a', 0), tag('c', 2)];
    expect(sortTags(tags, ['c', 'x', 'a']).map(t => t.id)).toEqual(['a', 'c']);
  });
});

describe('TAG_COLORS', () => {
  it('matches_backend_enum_order_and_has_labels', () => {
    expect(TAG_COLORS).toEqual(['Red', 'Orange', 'Amber', 'Yellow', 'Lime', 'Green', 'Teal', 'Cyan', 'Blue', 'Indigo', 'Purple', 'Pink']);
    expect(TAG_COLORS.map(c => TAG_COLOR_LABELS[c])).toEqual(
      ['紅', '橙', '琥珀', '黃', '萊姆', '綠', '藍綠', '青', '藍', '靛', '紫', '粉紅']);
  });
});
