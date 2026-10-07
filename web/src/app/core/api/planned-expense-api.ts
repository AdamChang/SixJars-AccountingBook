import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  GenerateResultDto, PayPlannedExpenseBody, PlannedExpenseDto, PlannedExpenseInput, RefreshResultDto,
} from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class PlannedExpenseApi {
  private readonly http = inject(HttpClient);

  list(bookId: string, budgetMonth: number): Observable<PlannedExpenseDto[]> {
    return this.http.get<PlannedExpenseDto[]>(this.url(bookId), { params: month(budgetMonth) });
  }

  create(bookId: string, input: PlannedExpenseInput): Observable<PlannedExpenseDto> {
    return this.http.post<PlannedExpenseDto>(this.url(bookId), input);
  }

  update(bookId: string, id: string, version: number, input: PlannedExpenseInput): Observable<PlannedExpenseDto> {
    return this.http.put<PlannedExpenseDto>(`${this.url(bookId)}/${id}`, { version, input });
  }

  delete(bookId: string, id: string, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(bookId)}/${id}`, { params: new HttpParams().set('version', version) });
  }

  pay(bookId: string, id: string, body: PayPlannedExpenseBody): Observable<unknown> {
    return this.http.post(`${this.url(bookId)}/${id}/pay`, body);
  }

  // 明確的產生與以現值更新（ADR 0009）
  generate(bookId: string, budgetMonth: number): Observable<GenerateResultDto> {
    return this.http.post<GenerateResultDto>(`${this.url(bookId)}/generate`, null, { params: month(budgetMonth) });
  }

  refresh(bookId: string, budgetMonth: number): Observable<RefreshResultDto> {
    return this.http.post<RefreshResultDto>(`${this.url(bookId)}/refresh`, null, { params: month(budgetMonth) });
  }

  private url(bookId: string): string {
    return `/api/books/${bookId}/planned-expenses`;
  }
}

function month(budgetMonth: number): HttpParams {
  return new HttpParams().set('budgetMonth', budgetMonth);
}
