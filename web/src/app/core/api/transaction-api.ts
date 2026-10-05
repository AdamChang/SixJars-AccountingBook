import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TransactionDto, TransactionInput } from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class TransactionApi {
  private readonly http = inject(HttpClient);

  list(bookId: string, budgetMonth: number): Observable<TransactionDto[]> {
    const params = new HttpParams().set('budgetMonth', budgetMonth);
    return this.http.get<TransactionDto[]>(`/api/books/${bookId}/transactions`, { params });
  }

  create(bookId: string, input: TransactionInput): Observable<TransactionDto> {
    return this.http.post<TransactionDto>(`/api/books/${bookId}/transactions`, input);
  }

  update(bookId: string, id: string, version: number, input: TransactionInput): Observable<TransactionDto> {
    return this.http.put<TransactionDto>(`/api/books/${bookId}/transactions/${id}`, { version, input });
  }

  delete(bookId: string, id: string, version: number): Observable<void> {
    const params = new HttpParams().set('version', version);
    return this.http.delete<void>(`/api/books/${bookId}/transactions/${id}`, { params });
  }
}
