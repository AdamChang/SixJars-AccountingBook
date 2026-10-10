// web/src/app/core/api/budget-api.ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { BudgetSheetDto, CategoryBudgetDto } from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class BudgetApi {
  private readonly http = inject(HttpClient);

  get(bookId: string, budgetMonth: number): Observable<BudgetSheetDto> {
    return this.http.get<BudgetSheetDto>(this.url(bookId), { params: new HttpParams().set('budgetMonth', budgetMonth) });
  }

  setDefault(bookId: string, categoryId: string, amount: number): Observable<CategoryBudgetDto> {
    return this.http.put<CategoryBudgetDto>(`${this.url(bookId)}/${categoryId}/default`, { amount });
  }

  removeDefault(bookId: string, categoryId: string): Observable<void> {
    return this.http.delete<void>(`${this.url(bookId)}/${categoryId}/default`);
  }

  setOverride(bookId: string, categoryId: string, budgetMonth: number, amount: number): Observable<CategoryBudgetDto> {
    return this.http.put<CategoryBudgetDto>(`${this.url(bookId)}/${categoryId}/overrides/${budgetMonth}`, { amount });
  }

  removeOverride(bookId: string, categoryId: string, budgetMonth: number): Observable<void> {
    return this.http.delete<void>(`${this.url(bookId)}/${categoryId}/overrides/${budgetMonth}`);
  }

  private url(bookId: string): string {
    return `/api/books/${bookId}/budgets`;
  }
}
