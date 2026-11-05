import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { NotificationsService } from './notifications.service';

describe('NotificationsService', () => {
  let service: NotificationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(NotificationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('keeps the unread badge in sync with the API and local reads', () => {
    service.refreshUnread().subscribe();
    http.expectOne('/api/notifications/unread-count').flush({ count: 3 });
    expect(service.unread()).toBe(3);

    service.markRead('n1').subscribe();
    http.expectOne({ method: 'POST', url: '/api/notifications/n1/read' }).flush(null);
    expect(service.unread()).toBe(2);

    service.markAllRead().subscribe();
    http.expectOne({ method: 'POST', url: '/api/notifications/read-all' }).flush({ marked: 2 });
    expect(service.unread()).toBe(0);
  });

  it('parses notification data defensively', () => {
    expect(NotificationsService.data({ notificationId: 'x', notificationType: 'Ticket.Created', createdAt: '', dataJson: '{"reference":"WI-2026-000001"}' }).reference).toBe('WI-2026-000001');
    expect(NotificationsService.data({ notificationId: 'x', notificationType: 'Ticket.Created', createdAt: '', dataJson: 'not json' })).toEqual({});
  });
});
