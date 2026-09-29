import { Injectable, signal } from '@angular/core';
import type { FirebaseApp } from 'firebase/app';
import type { Auth, User } from 'firebase/auth';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class FirebaseService {
  private appPromise?: Promise<FirebaseApp>;
  private authPromise?: Promise<Auth>;
  private readonly user = signal<User | null>(null);

  constructor() {
    if (this.configured) void this.getAuth().catch(() => this.user.set(null));
  }

  get enabled(): boolean { return environment.firebaseMode; }
  private get configured(): boolean {
    const config = environment.firebase;
    return this.enabled && !!(config.apiKey && config.authDomain && config.projectId && config.appId);
  }
  get currentUser(): User | null { return this.user(); }

  async getIdToken(): Promise<string | null> {
    if (!this.configured) return null;
    const auth = await this.getAuth();
    return auth.currentUser ? auth.currentUser.getIdToken() : null;
  }

  private getApp(): Promise<FirebaseApp> {
    if (!this.configured) return Promise.reject(new Error('Preencha a configuração Firebase em environment.firebase.ts.'));
    this.appPromise ??= import('firebase/app').then(({ initializeApp }) => initializeApp(environment.firebase));
    return this.appPromise;
  }

  private getAuth(): Promise<Auth> {
    this.authPromise ??= Promise.all([this.getApp(), import('firebase/auth')]).then(([app, authSdk]) => {
      const auth = authSdk.getAuth(app);
      authSdk.onAuthStateChanged(auth, user => this.user.set(user));
      return auth;
    });
    return this.authPromise;
  }

  async signIn(): Promise<User> {
    if (!this.configured) throw new Error('Preencha a configuração Firebase em environment.firebase.ts.');
    const [auth, authSdk] = await Promise.all([this.getAuth(), import('firebase/auth')]);
    const provider = new authSdk.GoogleAuthProvider();
    const user = (await authSdk.signInWithPopup(auth, provider)).user;
    this.user.set(user);
    return user;
  }

  async signOut(): Promise<void> {
    if (this.enabled) {
      const [auth, authSdk] = await Promise.all([this.getAuth(), import('firebase/auth')]);
      await authSdk.signOut(auth);
      this.user.set(null);
    }
  }

  async authorizeYoutube(): Promise<{ user: User; accessToken: string }> {
    if (!this.configured) throw new Error('Preencha a configuração Firebase em environment.firebase.ts.');
    const [auth, authSdk] = await Promise.all([this.getAuth(), import('firebase/auth')]);
    const provider = new authSdk.GoogleAuthProvider();
    provider.addScope('https://www.googleapis.com/auth/youtube.readonly');
    const result = await authSdk.signInWithPopup(auth, provider);
    const credential = authSdk.GoogleAuthProvider.credentialFromResult(result);
    if (!credential?.accessToken) throw new Error('O Google não retornou autorização para o YouTube.');
    this.user.set(result.user);
    return { user: result.user, accessToken: credential.accessToken };
  }

  async recordImport(user: User, channelCount: number): Promise<void> {
    if (!this.configured) throw new Error('Preencha a configuração Firebase em environment.firebase.ts.');
    const [app, firestoreSdk] = await Promise.all([this.getApp(), import('firebase/firestore')]);
    const firestore = firestoreSdk.getFirestore(app);
    await firestoreSdk.addDoc(firestoreSdk.collection(firestore, 'users', user.uid, 'actions'), {
      type: 'youtube-subscriptions-import',
      channelCount,
      createdAt: firestoreSdk.serverTimestamp()
    });
  }
}
