import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AddAccountBody, AddCategoryBody, AddPlanningFundBody, BookDto, ExpenseNature, ReorderBody, SettingPath,
} from './dto';

@Injectable({ providedIn: 'root' })
export class BookApi {
  private readonly http = inject(HttpClient);

  get(bookId: string): Observable<BookDto> {
    return this.http.get<BookDto>(`/api/books/${bookId}`);
  }

  add(bookId: string, path: 'accounts', body: AddAccountBody): Observable<{ id: string }>;
  add(bookId: string, path: 'planning-funds', body: AddPlanningFundBody): Observable<{ id: string }>;
  add(bookId: string, path: 'categories', body: AddCategoryBody): Observable<{ id: string }>;
  add(bookId: string, path: SettingPath, body: object): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`/api/books/${bookId}/${path}`, body);
  }

  updateAccount(bookId: string, id: string, body: { name: string; countsAsAvailableCash: boolean }): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/accounts/${id}`, body);
  }

  updatePlanningFund(bookId: string, id: string, body: { name: string }): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/planning-funds/${id}`, body);
  }

  // nature 為 null 時不修改性質；只有支出主分類可以修改
  updateCategory(bookId: string, id: string, body: { name: string; nature: ExpenseNature | null }): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/categories/${id}`, body);
  }

  archive(bookId: string, path: SettingPath, id: string): Observable<void> {
    return this.http.post<void>(`/api/books/${bookId}/${path}/${id}/archive`, null);
  }

  unarchive(bookId: string, path: SettingPath, id: string): Observable<void> {
    return this.http.post<void>(`/api/books/${bookId}/${path}/${id}/unarchive`, null);
  }

  remove(bookId: string, path: SettingPath, id: string): Observable<void> {
    return this.http.delete<void>(`/api/books/${bookId}/${path}/${id}`);
  }

  reorder(bookId: string, path: SettingPath, body: ReorderBody): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/${path}/order`, body);
  }

  // lockDate 為 null 時清除鎖帳日
  setLockDate(bookId: string, lockDate: string | null): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/lock-date`, { lockDate });
  }
}
