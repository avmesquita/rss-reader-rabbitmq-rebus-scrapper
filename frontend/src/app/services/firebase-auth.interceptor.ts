import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { FirebaseService } from './firebase.service';

const writeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const firebaseAuthInterceptor: HttpInterceptorFn = (request, next) => {
  if (!writeMethods.has(request.method) || !request.url.includes('/api/')) return next(request);

  const firebase = inject(FirebaseService);
  return from(firebase.getIdToken()).pipe(switchMap(token => {
    const outgoing = token
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;
    return next(outgoing);
  }));
};
