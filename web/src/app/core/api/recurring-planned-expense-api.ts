import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { RecurringPlannedExpenseDto, RecurringPlannedExpenseInput } from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class RecurringPlannedExpenseApi {
  private readonly http = inject(HttpClient);

  list(bookId: string): Observable<RecurringPlannedExpenseDto[]> {
    return this.http.get<RecurringPlannedExpenseDto[]>(this.url(bookId));
  }

  create(bookId: string, input: RecurringPlannedExpenseInput): Observable<RecurringPlannedExpenseDto> {
    return this.http.post<RecurringPlannedExpenseDto>(this.url(bookId), input);
  }

  update(bookId: string, id: string, version: number, input: RecurringPlannedExpenseInput): Observable<RecurringPlannedExpenseDto> {
    return this.http.put<RecurringPlannedExpenseDto>(`${this.url(bookId)}/${id}`, { version, input });
  }

  delete(bookId: string, id: string, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(bookId)}/${id}`, { params: new HttpParams().set('version', version) });
  }

  private url(bookId: string): string {
    return `/api/books/${bookId}/recurring-planned-expenses`;
  }
}
