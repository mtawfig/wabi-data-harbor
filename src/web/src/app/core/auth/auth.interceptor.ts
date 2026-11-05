import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const SESSION_PROBES = new Set(['/api/auth/me', '/api/auth/login', '/api/auth/csrf', '/api/auth/logout']);

/** Adds the antiforgery header to unsafe requests and reacts to a lost session with a redirect to login. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.csrfToken();
  const outgoing =
    token && UNSAFE_METHODS.has(request.method.toUpperCase())
      ? request.clone({ setHeaders: { 'X-XSRF-TOKEN': token } })
      : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !SESSION_PROBES.has(request.url) && auth.isAuthenticated()) {
        auth.sessionLost();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    }),
  );
};
