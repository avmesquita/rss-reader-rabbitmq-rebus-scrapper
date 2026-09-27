import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class WriteAccessSessionService {
  private readonly tokenKey = 'rss-reader.write-access-token';
  private token: string | null = this.readStoredToken();
  readonly authorized = signal(this.token !== null);
  readonly reauthenticationRequested = new Subject<void>();

  getToken(): string | null {
    return this.token;
  }

  setToken(token: string): void {
    this.token = token;
    this.authorized.set(true);
    try {
      sessionStorage.setItem(this.tokenKey, token);
    } catch {
      // Keep the authorization active in memory when browser storage is unavailable.
    }
  }

  setAuthorized(authorized: boolean): void {
    this.authorized.set(authorized);
    if (!authorized)
      this.clearToken();
  }

  invalidate(): void {
    this.clearToken();
    this.reauthenticationRequested.next();
  }

  private clearToken(): void {
    this.token = null;
    try {
      sessionStorage.removeItem(this.tokenKey);
    } catch {
      // Storage may be unavailable.
    }
  }

  private readStoredToken(): string | null {
    try {
      return sessionStorage.getItem(this.tokenKey);
    } catch {
      return null;
    }
  }
}
