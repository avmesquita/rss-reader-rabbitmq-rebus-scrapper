import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { WriteAccessSessionService } from './write-access-session.service';

const writeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const writeAccessInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(WriteAccessSessionService);
  const isUnlockRequest = request.url.startsWith('/api/access/unlock');
  const token = session.getToken();
  const outgoing = token && !isUnlockRequest
    ? request.clone({ setHeaders: { 'X-Write-Token': token } })
    : request;

  return next(outgoing).pipe(catchError(error => {
    if (error instanceof HttpErrorResponse && error.status === 401 && writeMethods.has(request.method) && !isUnlockRequest)
      session.invalidate();
    return throwError(() => error);
  }));
};
