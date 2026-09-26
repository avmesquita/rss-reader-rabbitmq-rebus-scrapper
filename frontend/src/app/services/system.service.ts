import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PurgeStats } from '../models';

@Injectable({ providedIn: 'root' })
export class SystemService {
  private readonly http = inject(HttpClient);

  purgeData(): Observable<PurgeStats> {
    return this.http.delete<PurgeStats>('/api/system/purge');
  }

}
