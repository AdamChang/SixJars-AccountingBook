import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { BookDto } from './dto';

@Injectable({ providedIn: 'root' })
export class BookApi {
  private readonly http = inject(HttpClient);

  get(bookId: string): Observable<BookDto> {
    return this.http.get<BookDto>(`/api/books/${bookId}`);
  }
}
