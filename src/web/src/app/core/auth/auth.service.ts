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
      const session = await firstValueFrom(this.http.post<Session>('/api/auth/login', { email, password }));
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

  /** Called by the HTTP interceptor when the API reports the session is gone. */
  sessionLost(): void {
    this.session.set(null);
  }

  private applySession(session: Session): void {
    this.session.set(session);
    if (session.preferredLanguage === 'ar' || session.preferredLanguage === 'en') {
      this.language.use(session.preferredLanguage);
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
