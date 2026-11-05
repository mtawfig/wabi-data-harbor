import { Dir } from '@angular/cdk/bidi';
import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LanguageService } from './core/i18n/language.service';

/**
 * Root shell. The CDK `dir` directive mirrors the active language direction so Angular Material layouts
 * (sidenav, menus, form fields) re-flow when the user switches between English and Arabic at runtime.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Dir],
  template: '<div class="app-root" [dir]="language.direction()"><router-outlet /></div>',
  styles: '.app-root { min-height: 100vh; }',
})
export class App {
  readonly language = inject(LanguageService);
}
