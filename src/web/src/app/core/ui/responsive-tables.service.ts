import { DOCUMENT } from '@angular/common';
import { Injectable, NgZone, inject } from '@angular/core';

/**
 * Phones show list tables as stacked cards (styles.scss, "Phones: list tables become cards"): every row is a block,
 * every cell a line with its column's name in front. CSS can only print that name if the cell carries it, so this
 * service copies each column header's text to its body cells (`data-label`) and keeps it right when rows arrive,
 * pages change or the interface language switches. It also marks the row's heading cell and gives the table parts
 * their ARIA roles, which browsers drop once a table is no longer displayed as a table.
 *
 * It covers every Angular Material table and every `table.plain` / `table.matrix`, present and future, with no
 * change in their templates. Result tables of an answer (`.ask__table`) are not list tables: they scroll sideways.
 * A table opts out with the class `wi-table-keep`.
 */
@Injectable({ providedIn: 'root' })
export class ResponsiveTablesService {
  static readonly tables = 'table.mat-mdc-table:not(.wi-table-keep), table.plain:not(.wi-table-keep), table.matrix:not(.wi-table-keep)';

  private readonly document = inject(DOCUMENT);
  private readonly zone = inject(NgZone);
  private observer: MutationObserver | null = null;
  private scheduled = false;

  /** Starts watching the page. Called once when the application starts. */
  start(): void {
    const view = this.document.defaultView;
    if (this.observer || !view || typeof view.MutationObserver === 'undefined') {
      return;
    }

    // Outside Angular: labelling cells never needs a change-detection pass.
    this.zone.runOutsideAngular(() => {
      this.observer = new view.MutationObserver(() => this.schedule());
      this.observer.observe(this.document.body, { childList: true, subtree: true, characterData: true });
    });
    this.schedule();
  }

  stop(): void {
    this.observer?.disconnect();
    this.observer = null;
  }

  /** One pass per frame, however many changes arrived. */
  private schedule(): void {
    if (this.scheduled) {
      return;
    }

    this.scheduled = true;
    const view = this.document.defaultView;
    const run = () => {
      this.scheduled = false;
      this.label();
    };
    if (view?.requestAnimationFrame) {
      view.requestAnimationFrame(run);
    } else {
      setTimeout(run);
    }
  }

  /** Labels the cells of every list table under `root` with their column's header text. */
  label(root: ParentNode = this.document): void {
    root.querySelectorAll<HTMLTableElement>(ResponsiveTablesService.tables).forEach((table) => this.labelTable(table));
  }

  private labelTable(table: HTMLTableElement): void {
    const headerRow = table.tHead?.rows[table.tHead.rows.length - 1];
    const headers: string[] = [];
    for (const cell of Array.from(headerRow?.cells ?? [])) {
      const text = (cell.textContent ?? '').replace(/\s+/g, ' ').trim();
      for (let span = 0; span < Math.max(1, cell.colSpan); span++) {
        headers.push(text);
      }

      set(cell, 'role', 'columnheader');
    }

    set(table, 'role', 'table');
    if (table.tHead) {
      set(table.tHead, 'role', 'rowgroup');
    }

    for (const row of Array.from(table.tHead?.rows ?? [])) {
      set(row, 'role', 'row');
    }

    for (const body of Array.from(table.tBodies)) {
      set(body, 'role', 'rowgroup');
      for (const row of Array.from(body.rows)) {
        set(row, 'role', 'row');
        let column = 0;
        let titled = false;
        for (const cell of Array.from(row.cells)) {
          // A cell across several columns (a message, a group heading) has no single column name.
          const label = cell.colSpan > 1 ? '' : (headers[column] ?? '');
          column += Math.max(1, cell.colSpan);
          set(cell, 'data-label', label);
          set(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
          // The first named cell is the row's heading on a phone.
          if (label && !titled) {
            titled = true;
            set(cell, 'data-cell', 'title');
          } else if (cell.getAttribute('data-cell') === 'title') {
            cell.removeAttribute('data-cell');
          }
        }
      }
    }
  }
}

/** Writes an attribute only when it changes; a role the template set itself is left alone. */
function set(element: Element, name: string, value: string): void {
  if (name === 'role') {
    if (!element.hasAttribute('role')) {
      element.setAttribute('role', value);
    }

    return;
  }

  if (element.getAttribute(name) !== value) {
    element.setAttribute(name, value);
  }
}
