import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PlatformInfo } from './platform-info';

/** Reads the public, non-sensitive product identity exposed by the API. */
@Injectable({ providedIn: 'root' })
export class PlatformInfoService {
  private readonly http = inject(HttpClient);

  getInfo(): Observable<PlatformInfo> {
    return this.http.get<PlatformInfo>('/api/platform/info');
  }
}
