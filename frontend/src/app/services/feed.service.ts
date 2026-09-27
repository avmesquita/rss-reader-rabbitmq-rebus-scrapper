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

  createFeed(name: string, url: string, description: string, pollIntervalMinutes: number): Observable<Feed> {
    return this.http.post<Feed>('/api/feeds', { name, url, description, pollIntervalMinutes });
  }

  updateFeed(feed: Feed, name: string, description: string, pollIntervalMinutes: number): Observable<Feed> {
    return this.http.put<Feed>(`/api/feeds/${feed.id}`, { name, description, pollIntervalMinutes });
  }

  refreshFeed(feedId: string): Observable<void> {
    return this.http.post<void>(`/api/feeds/${feedId}/refresh`, {});
  }

  deleteFeed(feedId: string): Observable<void> {
    return this.http.delete<void>(`/api/feeds/${feedId}`);
  }
}
