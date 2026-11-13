import { TestBed } from '@angular/core/testing';
import { ResponsiveTablesService } from './responsive-tables.service';

function host(html: string): HTMLElement {
  const element = document.createElement('div');
  element.innerHTML = html;
  document.body.appendChild(element);
  return element;
}

const labels = (row: HTMLTableRowElement) => Array.from(row.cells).map((c) => c.getAttribute('data-label'));

describe('ResponsiveTablesService', () => {
  let service: ResponsiveTablesService;
  let element: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ResponsiveTablesService);
  });

  afterEach(() => {
    service.stop();
    element?.remove();
  });

  it('gives every body cell its column name, marks the heading cell and leaves action cells unnamed', () => {
    element = host(`
      <table class="plain">
        <thead><tr><th> Name </th><th>Status</th><th></th></tr></thead>
        <tbody>
          <tr><td>Alpha</td><td>Active</td><td><button>Edit</button></td></tr>
          <tr><td>Beta</td><td>Paused</td><td></td></tr>
        </tbody>
      </table>`);
    service.label(element);

    const rows = element.querySelectorAll('tbody tr');
    expect(labels(rows[0] as HTMLTableRowElement)).toEqual(['Name', 'Status', '']);
    expect(labels(rows[1] as HTMLTableRowElement)).toEqual(['Name', 'Status', '']);
    expect((rows[0] as HTMLTableRowElement).cells[0].getAttribute('data-cell')).toBe('title');
    expect((rows[0] as HTMLTableRowElement).cells[1].hasAttribute('data-cell')).toBe(false);
  });

  it('keeps the table readable to assistive technology once it is no longer laid out as a table', () => {
    element = host('<table class="mat-mdc-table"><thead><tr><th>Name</th></tr></thead><tbody><tr><td>Alpha</td></tr></tbody></table>');
    service.label(element);

    expect(element.querySelector('table')!.getAttribute('role')).toBe('table');
    expect(element.querySelector('thead')!.getAttribute('role')).toBe('rowgroup');
    expect(element.querySelector('tbody tr')!.getAttribute('role')).toBe('row');
    expect(element.querySelector('th')!.getAttribute('role')).toBe('columnheader');
    expect(element.querySelector('td')!.getAttribute('role')).toBe('cell');
  });

  it('does not overwrite a role the template set itself', () => {
    element = host('<table class="plain" role="presentation"><thead><tr><th>Name</th></tr></thead><tbody><tr><td>Alpha</td></tr></tbody></table>');
    service.label(element);
    expect(element.querySelector('table')!.getAttribute('role')).toBe('presentation');
  });

  it('follows header cells that span columns, and leaves a cell across the row unnamed', () => {
    element = host(`
      <table class="matrix">
        <thead><tr><th colspan="2">Object</th><th>Access</th></tr></thead>
        <tbody>
          <tr><td>dbo</td><td>Flights</td><td>Granted</td></tr>
          <tr><td colspan="3">Nothing to show</td></tr>
        </tbody>
      </table>`);
    service.label(element);

    const rows = element.querySelectorAll('tbody tr');
    expect(labels(rows[0] as HTMLTableRowElement)).toEqual(['Object', 'Object', 'Access']);
    expect(labels(rows[1] as HTMLTableRowElement)).toEqual(['']);
    expect((rows[1] as HTMLTableRowElement).cells[0].hasAttribute('data-cell')).toBe(false);
  });

  it('leaves answer result tables and opted-out tables alone', () => {
    element = host(`
      <table class="ask__table"><thead><tr><th>Month</th></tr></thead><tbody><tr><td>January</td></tr></tbody></table>
      <table class="plain wi-table-keep"><thead><tr><th>Month</th></tr></thead><tbody><tr><td>January</td></tr></tbody></table>`);
    service.label(element);
    element.querySelectorAll('td').forEach((cell) => expect(cell.hasAttribute('data-label')).toBe(false));
  });

  it('relabels when the header text changes (another language) and when rows arrive', async () => {
    element = host('<table class="plain"><thead><tr><th>Name</th></tr></thead><tbody><tr><td>Alpha</td></tr></tbody></table>');
    service.start();
    const settled = () => new Promise((resolve) => setTimeout(resolve, 60));
    await settled();
    expect(element.querySelector('td')!.getAttribute('data-label')).toBe('Name');

    element.querySelector('th')!.textContent = 'الاسم';
    const row = document.createElement('tr');
    row.innerHTML = '<td>Beta</td>';
    element.querySelector('tbody')!.appendChild(row);
    await settled();

    element.querySelectorAll('td').forEach((cell) => expect(cell.getAttribute('data-label')).toBe('الاسم'));
  });
});
