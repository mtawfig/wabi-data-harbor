import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AnswerFeedbackState, AnswerVerdict, VerifiedAnswerAction, VerifiedAnswerCounts, VerifiedAnswerDetail, VerifiedAnswerPage } from './verified-answers.models';

/** Verified answers (P13.53): the asker's verdict on an answer, and the Organization Administrators' review. */
@Injectable({ providedIn: 'root' })
export class VerifiedAnswersService {
  private readonly http = inject(HttpClient);

  feedback(historyId: string, versionNumber: number): Observable<AnswerFeedbackState> {
    return this.http.get<AnswerFeedbackState>(`/api/history/${historyId}/versions/${versionNumber}/feedback`);
  }

  giveFeedback(historyId: string, versionNumber: number, verdict: AnswerVerdict, reason: string | null): Observable<AnswerFeedbackState> {
    return this.http.post<AnswerFeedbackState>(`/api/history/${historyId}/versions/${versionNumber}/feedback`, { verdict, reason });
  }

  list(page: number, pageSize: number, status: string | null, applicationId: string | null): Observable<VerifiedAnswerPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) {
      params = params.set('status', status);
    }
    if (applicationId) {
      params = params.set('applicationId', applicationId);
    }
    return this.http.get<VerifiedAnswerPage>('/api/verified-answers', { params });
  }

  counts(): Observable<VerifiedAnswerCounts> {
    return this.http.get<VerifiedAnswerCounts>('/api/verified-answers/counts');
  }

  get(id: string): Observable<VerifiedAnswerDetail> {
    return this.http.get<VerifiedAnswerDetail>(`/api/verified-answers/${id}`);
  }

  review(id: string, action: VerifiedAnswerAction, note: string | null): Observable<VerifiedAnswerDetail> {
    return this.http.post<VerifiedAnswerDetail>(`/api/verified-answers/${id}/review`, { action, note });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/verified-answers/${id}`);
  }
}
