import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SettingDefinition, UpdateSettingRequest } from '../models/settings.models';
import { PropertyBagResponse } from '../models/property-bag.models';

export interface SyncStatusResponse {
  lastSyncDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private http = inject(HttpClient);
  private base = '/api/v1/settings';

  getAll(): Observable<SettingDefinition[]> {
    return this.http.get<SettingDefinition[]>(this.base);
  }

  getSyncStatus(): Observable<SyncStatusResponse> {
    return this.http.get<SyncStatusResponse>(`${this.base}/sync-status`);
  }

  update(key: string, request: UpdateSettingRequest): Observable<PropertyBagResponse> {
    return this.http.put<PropertyBagResponse>(`${this.base}/${key}`, request);
  }
}
