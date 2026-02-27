import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PropertyBagResponse, UpsertPropertyBagRequest } from '../models/property-bag.models';

@Injectable({ providedIn: 'root' })
export class PropertyBagService {
  private http = inject(HttpClient);
  private base = '/api/v1/property-bag';

  getAll(): Observable<PropertyBagResponse[]> {
    return this.http.get<PropertyBagResponse[]>(this.base);
  }

  get(key: string): Observable<PropertyBagResponse> {
    return this.http.get<PropertyBagResponse>(`${this.base}/${key}`);
  }

  upsert(key: string, request: UpsertPropertyBagRequest): Observable<PropertyBagResponse> {
    return this.http.put<PropertyBagResponse>(`${this.base}/${key}`, request);
  }

  delete(key: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${key}`);
  }
}
