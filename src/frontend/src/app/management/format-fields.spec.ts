import { TestBed } from '@angular/core/testing';
import { FormatDraft, newDraft } from './format-draft';
import { FormatFields } from './format-fields';
import { scanInto } from './test-support';

function open(idBase: string, draft: FormatDraft = newDraft()) {
  const fixture = TestBed.createComponent(FormatFields);
  fixture.componentRef.setInput('draft', draft);
  fixture.componentRef.setInput('idBase', idBase);
  const patches: Partial<FormatDraft>[] = [];
  fixture.componentInstance.patch.subscribe((patch) => patches.push(patch));
  return { fixture, patches, root: fixture.nativeElement as HTMLElement };
}

describe('FormatFields: barcode scanner (ADR 0015)', () => {
  it('fills only its own barcode field from a scan, and keeps Enter from saving', async () => {
    const first = open('f0');
    const second = open('f1');
    await first.fixture.whenStable();
    await second.fixture.whenStable();
    const field = first.root.querySelector<HTMLInputElement>('#f0-code-barres')!;
    document.body.append(first.root, second.root);
    field.focus();

    const end = scanInto(field, '6111000000011');

    expect(first.patches).toContainEqual({ barcode: '6111000000011' });
    expect(second.patches.filter((patch) => 'barcode' in patch && patch.barcode === '6111000000011')).toEqual([]);
    expect(end!.defaultPrevented).toBe(true);
    first.root.remove();
    second.root.remove();
  });
});
