import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ProblemDetails, problemCode } from '../auth/session.model';
import { DemoAssignee, DemoRequestDetail, DemoRequestFollowUp, DemoRequestForm, DemoRequestPage, DemoRequestSummary } from './demo-requests.models';

export interface DemoRequestFilters {
  status?: string | null;
  country?: string | null;
  from?: string | null;
  to?: string | null;
  search?: string | null;
}

/** Demo requests (P13.52): the public form's submission, and the administrator's list, detail and follow-up. */
@Injectable({ providedIn: 'root' })
export class DemoRequestsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private availabilityRequest: Promise<boolean> | null = null;

  /**
   * Whether the public form is open (P13.56). Asked once per visit; anything but a clear "yes" (also a failed request)
   * counts as closed, so a closed form never shows a button or opens.
   */
  available(): Promise<boolean> {
    this.availabilityRequest ??= firstValueFrom(this.http.get<{ enabled: boolean }>('/api/public/demo-requests/availability'))
      .then((answer) => answer?.enabled === true)
      .catch(() => false);
    return this.availabilityRequest;
  }

  /** Sends the public form. A visitor has no session: an antiforgery token that no longer matches is renewed and the form sent once more. */
  async submit(form: DemoRequestForm): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/public/demo-requests', form));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 400 && problemCode(error.error as ProblemDetails) === 'antiforgery') {
        await this.auth.refreshCsrf();
        await firstValueFrom(this.http.post<void>('/api/public/demo-requests', form));
        return;
      }
      throw error;
    }
  }

  list(page: number, pageSize: number, filters: DemoRequestFilters): Observable<DemoRequestPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    for (const [key, value] of Object.entries(filters)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http.get<DemoRequestPage>('/api/platform/demo-requests', { params });
  }

  summary(): Observable<DemoRequestSummary> {
    return this.http.get<DemoRequestSummary>('/api/platform/demo-requests/summary');
  }

  assignees(): Observable<DemoAssignee[]> {
    return this.http.get<DemoAssignee[]>('/api/platform/demo-requests/assignees');
  }

  get(id: string): Observable<DemoRequestDetail> {
    return this.http.get<DemoRequestDetail>(`/api/platform/demo-requests/${id}`);
  }

  update(id: string, followUp: DemoRequestFollowUp & { note?: string | null }): Observable<DemoRequestDetail> {
    return this.http.put<DemoRequestDetail>(`/api/platform/demo-requests/${id}`, followUp);
  }
}
