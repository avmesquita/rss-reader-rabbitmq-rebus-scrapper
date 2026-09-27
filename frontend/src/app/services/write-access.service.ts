import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { WriteAccessSessionService } from './write-access-session.service';

interface WriteAccessStatus {
  configured: boolean;
  authorized: boolean;
  contactEmail: string | null;
}

interface UnlockResponse {
  token: string;
  expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class WriteAccessService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(WriteAccessSessionService);

  readonly configured = signal(false);
  readonly contactEmail = signal<string | null>(null);
  readonly statusLoaded = signal(false);
  readonly statusUnavailable = signal(false);

  refreshStatus(): void {
    this.http.get<WriteAccessStatus>('/api/access/status').subscribe({
      next: status => {
        this.configured.set(status.configured);
        this.contactEmail.set(status.contactEmail);
        this.session.setAuthorized(status.configured && status.authorized);
        this.statusUnavailable.set(false);
        this.statusLoaded.set(true);
      },
      error: () => {
        this.session.setAuthorized(false);
        this.statusUnavailable.set(true);
        this.statusLoaded.set(true);
      }
    });
  }

  unlock(password: string): Observable<void> {
    return this.http.post<UnlockResponse>('/api/access/unlock', { password }).pipe(
      tap(response => this.session.setToken(response.token)),
      tap(() => this.configured.set(true)),
      tap(() => this.statusLoaded.set(true)),
      tap(() => this.statusUnavailable.set(false)),
      map(() => undefined)
    );
  }
}
