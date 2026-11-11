import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LanguageService } from '../i18n/language.service';
import { ORGANIZATION_ADMIN_ROLE, ProblemDetails, Session, problemCode } from './session.model';

export interface LoginFailure {
  code: string;
  status: number;
}

/**
 * Session state for the Angular app. The cookie is HttpOnly, so the client only mirrors what
 * GET /api/auth/me reports; every authorization decision is still made by the API.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  readonly session = signal<Session | null>(null);
  readonly initialized = signal(false);
  readonly csrfToken = signal<string | null>(null);

  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly isPlatformAdministrator = computed(() => this.session()?.principalType === 'PlatformAdministrator');
  readonly isOrganizationUser = computed(() => this.session()?.principalType === 'OrganizationUser');
  readonly isOrganizationAdmin = computed(() => this.session()?.roles.includes(ORGANIZATION_ADMIN_ROLE) ?? false);
  readonly isManagement = computed(() => this.session()?.roles.includes('Management') ?? false);
  /** Monitoring Rules (Phase 14): Organization Administrators and Management users author rules. */
  readonly canAuthorRules = computed(() => this.isOrganizationAdmin() || this.isManagement());
  readonly mustChangePassword = computed(() => this.session()?.mustChangePassword ?? false);

  constructor() {
    // A language picked with the switcher while signed in is saved on the account (C10).
    this.language.chosen.subscribe((language) => {
      if (this.session()) {
        void this.saveLanguage(language);
      }
    });
  }

  /** Called once at startup: fetch an antiforgery token and restore the session if the cookie is valid. */
  async initialize(): Promise<void> {
    try {
      await this.refreshCsrf();
      const session = await firstValueFrom(this.http.get<Session>('/api/auth/me'));
      this.applySession(session);
    } catch {
      this.session.set(null);
    } finally {
      this.initialized.set(true);
    }
  }

  async refreshCsrf(): Promise<void> {
    const response = await firstValueFrom(this.http.get<{ token: string; headerName: string }>('/api/auth/csrf'));
    this.csrfToken.set(response.token);
  }

  async login(email: string, password: string): Promise<Session> {
    try {
      const session = await this.postLogin(email, password);
      await this.refreshCsrf();
      this.applySession(session);
      return session;
    } catch (error) {
      throw this.toLoginFailure(error);
    }
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.session.set(null);
      await this.refreshCsrf().catch(() => undefined);
      await this.router.navigateByUrl('/login');
    }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<Session> {
    const session = await firstValueFrom(this.http.post<Session>('/api/auth/change-password', { currentPassword, newPassword }));
    await this.refreshCsrf();
    this.applySession(session);
    return session;
  }

  /**
   * Self-service reset, step 1: asks for a reset link by e-mail. The API answers the same way for every address, so
   * this resolves whether or not the address has an account.
   */
  async requestPasswordReset(email: string): Promise<void> {
    await this.postPublic('/api/auth/forgot-password', { email });
  }

  /** Whether the token of a reset link can still be used. */
  async isResetLinkValid(token: string): Promise<boolean> {
    return (await this.postPublic<{ valid: boolean }>('/api/auth/reset-password/check', { token })).valid;
  }

  /** Self-service reset, step 2: sets the new password. Nobody is signed in by it; any session this browser held is over. */
  async resetPassword(token: string, newPassword: string): Promise<void> {
    await this.postPublic('/api/auth/reset-password', { token, newPassword });
    this.session.set(null);
    await this.refreshCsrf().catch(() => undefined);
  }

  /** A request made while signed out: an antiforgery token that no longer matches is renewed and the request tried once more. */
  private async postPublic<T = void>(url: string, body: unknown): Promise<T> {
    try {
      return await firstValueFrom(this.http.post<T>(url, body));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 400 && problemCode(error.error as ProblemDetails) === 'antiforgery') {
        await this.refreshCsrf();
        return await firstValueFrom(this.http.post<T>(url, body));
      }
      throw error;
    }
  }

  /** The landing route for the current session: platform overview, or the organization workspace. */
  homeUrl(): string {
    const session = this.session();
    if (!session) {
      return '/login';
    }
    if (session.mustChangePassword) {
      return '/change-password';
    }
    return session.principalType === 'PlatformAdministrator' ? '/platform/overview' : '/app/ask';
  }

  /**
   * Called by the HTTP interceptor when the API reports the session is gone. The antiforgery token was bound to the old
   * session, so a fresh one is fetched at once; otherwise the first sign-in afterwards would be refused (F1).
   */
  sessionLost(): void {
    this.session.set(null);
    void this.refreshCsrf().catch(() => undefined);
  }

  /** Signs in; a token that no longer matches (an expired session, a token fetched in another tab) is renewed and the sign-in tried once more. */
  private async postLogin(email: string, password: string): Promise<Session> {
    try {
      return await firstValueFrom(this.http.post<Session>('/api/auth/login', { email, password }));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 400 && problemCode(error.error as ProblemDetails) === 'antiforgery') {
        await this.refreshCsrf();
        return await firstValueFrom(this.http.post<Session>('/api/auth/login', { email, password }));
      }
      throw error;
    }
  }

  /**
   * The browser's own choice wins over the account's stored language, so switching and then reloading never flips the
   * interface back; the account is brought up to date so other devices follow. Without a choice in this browser, the
   * account's language applies.
   */
  private applySession(session: Session): void {
    this.session.set(session);
    const stored = this.language.storedChoice();
    if (stored) {
      this.language.use(stored);
      if (stored !== session.preferredLanguage && !session.mustChangePassword) {
        void this.saveLanguage(stored);
      }
    } else if (session.preferredLanguage === 'ar' || session.preferredLanguage === 'en') {
      this.language.use(session.preferredLanguage, false);
    }
  }

  private async saveLanguage(language: 'en' | 'ar'): Promise<void> {
    try {
      const session = await firstValueFrom(this.http.put<Session>('/api/auth/language', { language }));
      this.session.update((current) => (current ? { ...current, preferredLanguage: session.preferredLanguage } : current));
    } catch {
      // The interface already shows the chosen language and this browser keeps it; the account catches up next time.
    }
  }

  private toLoginFailure(error: unknown): LoginFailure {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 429) {
        return { code: 'rate_limited', status: 429 };
      }
      const code = problemCode(error.error as ProblemDetails);
      return { code: code ?? 'unknown', status: error.status };
    }
    return { code: 'unknown', status: 0 };
  }
}
