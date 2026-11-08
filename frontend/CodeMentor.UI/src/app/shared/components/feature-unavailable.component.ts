import { Component, input } from '@angular/core';

@Component({
  selector: 'app-feature-unavailable',
  template: `
    <section class="unavailable" role="status">
      <span class="unavailable-icon" aria-hidden="true">⌁</span>
      <div>
        <h2>{{ title() }}</h2>
        <p>{{ description() }}</p>
        <div class="endpoint">Backend endpoint not available: <code>{{ endpoint() }}</code></div>
      </div>
    </section>
  `,
  styles: `
    .unavailable { display: flex; align-items: flex-start; gap: 16px; border: 1px solid #e5e6f4; border-radius: 14px; background: #f7f7fc; padding: 20px; }
    .unavailable-icon { display: grid; flex: 0 0 38px; height: 38px; place-items: center; border-radius: 11px; background: #ecebff; color: #635bcd; font-size: 22px; }
    h2 { color: #29314b; font-size: 15px; margin: 1px 0 6px; }
    p { color: #737b91; font-size: 13px; line-height: 1.6; margin: 0; }
    .endpoint { color: #81879a; font-size: 11px; margin-top: 10px; }
    code { color: #5a55ac; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  `,
})
export class FeatureUnavailableComponent {
  readonly title = input.required<string>();
  readonly description = input.required<string>();
  readonly endpoint = input.required<string>();
}
