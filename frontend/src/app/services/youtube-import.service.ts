import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { FirebaseService } from './firebase.service';

interface YoutubeSubscriptionPage {
  items?: Array<{ snippet?: { title?: string; resourceId?: { channelId?: string } } }>;
  nextPageToken?: string;
}

@Injectable({ providedIn: 'root' })
export class YoutubeImportService {
  private readonly http = inject(HttpClient);
  private readonly firebase = inject(FirebaseService);

  async importSubscriptions(): Promise<number> {
    if (!environment.apiBaseUrl) throw new Error('Preencha a URL pública da API em environment.firebase.ts.');
    const { user, accessToken } = await this.firebase.authorizeYoutube();
    const headers = { Authorization: `Bearer ${accessToken}` };
    const channels = new Map<string, string>();
    let pageToken: string | undefined;

    do {
      const page = await firstValueFrom(this.http.get<YoutubeSubscriptionPage>(
        'https://www.googleapis.com/youtube/v3/subscriptions',
        { headers, params: { part: 'snippet', mine: 'true', maxResults: '50', ...(pageToken ? { pageToken } : {}) } }
      ));
      for (const item of page.items ?? []) {
        const channelId = item.snippet?.resourceId?.channelId;
        if (channelId) channels.set(channelId, item.snippet?.title || channelId);
      }
      pageToken = page.nextPageToken;
    } while (pageToken);

    const channelList = [...channels].map(([channelId, title]) => ({ channelId, title }));
    const idToken = await user.getIdToken();
    for (let offset = 0; offset < channelList.length; offset += 500) {
      await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/api/youtube/subscriptions/import`, {
        channels: channelList.slice(offset, offset + 500)
      }, { headers: { Authorization: `Bearer ${idToken}` } }));
    }
    try {
      await this.firebase.recordImport(user, channels.size);
    } catch {
      throw new Error('A importação foi enviada para a API, mas não foi possível registrar a ação no Firestore.');
    }
    return channels.size;
  }
}
