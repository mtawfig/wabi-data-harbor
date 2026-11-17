import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { DemoRequestsService } from './demo-requests.service';

/** The "Request a demo" page opens only while the form is switched on; otherwise everyone lands on the home page (P13.56). */
export const demoRequestsOpenGuard: CanActivateFn = async () => {
  const router = inject(Router);
  return (await inject(DemoRequestsService).available()) ? true : router.createUrlTree(['/']);
};
