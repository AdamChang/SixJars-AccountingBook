import { Injectable } from '@angular/core';

// 包住 window.location，讓測試可以替換（jsdom 的 location.assign 不能 spy）
@Injectable({ providedIn: 'root' })
export class BrowserLocation {
  assign(url: string): void {
    window.location.assign(url);
  }

  currentPath(): string {
    return window.location.pathname + window.location.search;
  }
}
