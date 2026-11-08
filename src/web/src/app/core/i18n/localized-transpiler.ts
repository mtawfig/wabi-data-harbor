import { Injectable, Injector, inject } from '@angular/core';
import { DefaultTranspiler, TranslocoService, TranspileParams } from '@jsverse/transloco';
import { localizeParams } from '../../shared/format/locale-format';

/**
 * Numbers passed into translated sentences follow the interface language: "{{count}} مرات" reads "٦ مرات" in Arabic,
 * like every other number on an Arabic page. English sentences are unchanged.
 */
@Injectable()
export class LocalizedTranspiler extends DefaultTranspiler {
  private readonly injector = inject(Injector);

  override transpile(params: TranspileParams): unknown {
    if (params.params) {
      // Resolved lazily: the service itself depends on the transpiler.
      const language = this.injector.get(TranslocoService).getActiveLang();
      return super.transpile({ ...params, params: localizeParams(params.params, language) });
    }
    return super.transpile(params);
  }
}
