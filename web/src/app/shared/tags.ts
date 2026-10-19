import { TagColor, TagDto } from '../core/api/dto';

// 順序與後端 enum TagColor 相同（tags spec G22）；色碼在 styles.scss 的 --tag-<color>-bg／-fg
export const TAG_COLORS: readonly TagColor[] =
  ['Red', 'Orange', 'Amber', 'Yellow', 'Lime', 'Green', 'Teal', 'Cyan', 'Blue', 'Indigo', 'Purple', 'Pink'];

export const TAG_COLOR_LABELS: Record<TagColor, string> = {
  Red: '紅', Orange: '橙', Amber: '琥珀', Yellow: '黃', Lime: '萊姆', Green: '綠',
  Teal: '藍綠', Cyan: '青', Blue: '藍', Indigo: '靛', Purple: '紫', Pink: '粉紅',
};

// 名稱上限，以 grapheme cluster 計算（一個 emoji 算 1 個字），與後端 StringInfo 一致（F4、後端 G5）
export const TAG_NAME_MAX_LENGTH = 8;
// 交易最多掛幾個標籤（後端 G2）
export const MAX_TAGS_PER_TRANSACTION = 4;

const segmenter = new Intl.Segmenter('zh-Hant', { granularity: 'grapheme' });

export function textLength(text: string): number {
  return [...segmenter.segment(text.trim())].length;
}

// 新增標籤的預設顏色：還沒有標籤用過的第一個顏色，全部用過就回到第一個（F3）
export function nextTagColor(used: TagColor[]): TagColor {
  return TAG_COLORS.find(color => !used.includes(color)) ?? TAG_COLORS[0];
}

// 交易上的標籤依標籤的設定排序顯示（後端 G20）；找不到的 id 略過
export function sortTags(tags: TagDto[], ids: string[]): TagDto[] {
  return tags.filter(tag => ids.includes(tag.id)).sort((a, b) => a.sortOrder - b.sortOrder);
}

export function tagColorVar(color: TagColor, part: 'bg' | 'fg'): string {
  return `var(--tag-${color.toLowerCase()}-${part})`;
}
