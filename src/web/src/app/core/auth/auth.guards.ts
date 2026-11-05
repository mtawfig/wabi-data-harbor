import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Requires a session; otherwise redirects to login with a return URL. Guards are UX only: the API enforces authorization. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) {
    return auth.mustChangePassword() && !state.url.startsWith('/change-password')
      ? router.createUrlTree(['/change-password'])
      : true;
  }
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const platformAdministratorGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isPlatformAdministrator() ? true : router.createUrlTree([auth.homeUrl()]);
};

export const organizationUserGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isOrganizationUser() ? true : router.createUrlTree([auth.homeUrl()]);
};

export const organizationAdminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isOrganizationAdmin() ? true : router.createUrlTree([auth.homeUrl()]);
};

/** Keeps signed-in users away from the public login page. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() ? router.createUrlTree([auth.homeUrl()]) : true;
};
