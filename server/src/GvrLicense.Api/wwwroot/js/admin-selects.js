/**
 * Select + search (Tom Select) for admin dropdowns marked .js-searchable.
 *
 * In Bootstrap modals the dropdown must stay interactable: appending to body with a high
 * z-index is fine ONLY if the modal does not trap focus (data-bs-focus="false"). Otherwise
 * the menu opens behind / outside the trap and looks like a z-index bug.
 */
(function () {
  'use strict';

  function placeDropdown(ts) {
    const dropdown = ts.dropdown;
    const control = ts.control;
    if (!dropdown || !control) return;

    const rect = control.getBoundingClientRect();
    dropdown.style.position = 'fixed';
    dropdown.style.top = Math.round(rect.bottom + 2) + 'px';
    dropdown.style.left = Math.round(rect.left) + 'px';
    dropdown.style.width = Math.round(rect.width) + 'px';
    dropdown.style.minWidth = Math.round(rect.width) + 'px';
    dropdown.style.maxWidth = Math.round(rect.width) + 'px';
    dropdown.style.right = 'auto';
    // Above Bootstrap modal (1055) and AdminLTE layers.
    dropdown.style.zIndex = '20000';
  }

  function ensureModalAllowsOutsideFocus(el) {
    const modal = el.closest('.modal');
    if (!modal) return;
    // Bootstrap 5 focus trap blocks clicks on body-mounted Tom Select menus.
    modal.setAttribute('data-bs-focus', 'false');
  }

  function initSelect(el) {
    if (!el || typeof TomSelect === 'undefined') return;

    if (el.tomselect) {
      el.tomselect.destroy();
    }

    ensureModalAllowsOutsideFocus(el);

    const ts = new TomSelect(el, {
      create: false,
      allowEmptyOption: true,
      maxOptions: null,
      openOnFocus: true,
      hideSelected: false,
      placeholder: 'Buscar…',
      sortField: { field: 'text', direction: 'asc' },
      plugins: ['dropdown_input', 'clear_button'],
      dropdownParent: document.body,
      onDropdownOpen: function () {
        placeDropdown(this);
        const input = this.control_input;
        if (input) {
          setTimeout(() => {
            try { input.focus(); } catch (_) { /* ignore */ }
          }, 0);
        }
      },
    });

    const reposition = () => {
      if (ts.isOpen) placeDropdown(ts);
    };
    window.addEventListener('resize', reposition);
    document.addEventListener('scroll', reposition, true);

    ts.on('destroy', () => {
      window.removeEventListener('resize', reposition);
      document.removeEventListener('scroll', reposition, true);
    });
  }

  function initAll(root) {
    (root || document).querySelectorAll('select.form-select.js-searchable').forEach(initSelect);
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('select.form-select.js-searchable').forEach((el) => {
      if (el.closest('.modal')) return;
      initSelect(el);
    });
  });

  // Before the modal is shown: disable focus trap if it hosts searchable selects.
  document.addEventListener('show.bs.modal', (e) => {
    if (!e.target) return;
    if (e.target.querySelector('select.form-select.js-searchable')) {
      e.target.setAttribute('data-bs-focus', 'false');
    }
  });

  document.addEventListener('shown.bs.modal', (e) => {
    if (e.target) initAll(e.target);
  });

  document.addEventListener('hide.bs.modal', (e) => {
    if (!e.target) return;
    e.target.querySelectorAll('select.form-select.js-searchable').forEach((el) => {
      if (el.tomselect) el.tomselect.close();
    });
  });

  window.gvrInitSearchableSelects = initAll;
})();
