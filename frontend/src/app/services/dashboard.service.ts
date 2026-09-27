import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Dashboard, DebugStatus, QueueMessage } from '../models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getDashboard(): Observable<Dashboard> {
    return this.http.get<Dashboard>('/api/dashboard');
  }

  getDebugStatus(): Observable<DebugStatus> {
    return this.http.get<DebugStatus>('/api/debug');
  }

  getQueueMessages(queueName: string): Observable<QueueMessage[]> {
    return this.http.get<QueueMessage[]>(`/api/debug/queues/${encodeURIComponent(queueName)}/messages`, {
      params: { limit: 50 }
    });
  }
}
