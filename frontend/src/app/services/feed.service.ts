import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Feed } from '../models';

@Injectable({ providedIn: 'root' })
export class FeedService {
  private readonly http = inject(HttpClient);

  getFeeds(): Observable<Feed[]> {
    return this.http.get<Feed[]>('/api/feeds');
  }

  createFeed(name: string, url: string): Observable<Feed> {
    return this.http.post<Feed>('/api/feeds', { name, url });
  }

  refreshFeed(feedId: string): Observable<void> {
    return this.http.post<void>(`/api/feeds/${feedId}/refresh`, {});
  }

  deleteFeed(feedId: string): Observable<void> {
    return this.http.delete<void>(`/api/feeds/${feedId}`);
  }
}
