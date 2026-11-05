import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../organizations/organizations.models';
import { CreateTicketRequest, SupportArea, TicketAttachment, TicketComment, TicketDetail, TicketEvent, TicketListQuery, TicketStatus, TicketSummary } from './support.models';

/** Support desk API. The same operations exist under /api/support (organization area) and /api/platform/support (platform queue). */
@Injectable({ providedIn: 'root' })
export class SupportService {
  private readonly http = inject(HttpClient);

  static base(area: SupportArea): string {
    return area === 'platform' ? '/api/platform/support/tickets' : '/api/support/tickets';
  }

  list(area: SupportArea, query: TicketListQuery): Observable<PagedResult<TicketSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const optional: [string, string | boolean | null | undefined][] = [
      ['search', query.search],
      ['status', query.status],
      ['open', query.open],
      ['ticketType', query.ticketType],
      ['impact', query.impact],
      ['organizationId', query.organizationId],
      ['assignedAdministratorId', query.assignedAdministratorId],
      ['unassigned', query.unassigned],
      ['sort', query.sort],
      ['descending', query.descending],
    ];
    for (const [key, value] of optional) {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PagedResult<TicketSummary>>(SupportService.base(area), { params });
  }

  create(request: CreateTicketRequest): Observable<TicketDetail> {
    return this.http.post<TicketDetail>(SupportService.base('app'), request);
  }

  get(area: SupportArea, id: string): Observable<TicketDetail> {
    return this.http.get<TicketDetail>(`${SupportService.base(area)}/${id}`);
  }

  events(area: SupportArea, id: string): Observable<TicketEvent[]> {
    return this.http.get<TicketEvent[]>(`${SupportService.base(area)}/${id}/events`);
  }

  comments(area: SupportArea, id: string): Observable<TicketComment[]> {
    return this.http.get<TicketComment[]>(`${SupportService.base(area)}/${id}/comments`);
  }

  addComment(area: SupportArea, id: string, body: string): Observable<TicketComment> {
    return this.http.post<TicketComment>(`${SupportService.base(area)}/${id}/comments`, { body });
  }

  changeStatus(area: SupportArea, id: string, status: TicketStatus, note?: string): Observable<TicketDetail> {
    return this.http.post<TicketDetail>(`${SupportService.base(area)}/${id}/status`, { status, note: note || null });
  }

  assign(id: string, administratorId: string | null): Observable<TicketDetail> {
    return this.http.post<TicketDetail>(`${SupportService.base('platform')}/${id}/assign`, { administratorId });
  }

  attachments(area: SupportArea, id: string): Observable<TicketAttachment[]> {
    return this.http.get<TicketAttachment[]>(`${SupportService.base(area)}/${id}/attachments`);
  }

  upload(area: SupportArea, id: string, file: File): Observable<TicketAttachment> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<TicketAttachment>(`${SupportService.base(area)}/${id}/attachments`, form);
  }

  downloadUrl(area: SupportArea, id: string, attachmentId: string): string {
    return `${SupportService.base(area)}/${id}/attachments/${attachmentId}`;
  }
}
