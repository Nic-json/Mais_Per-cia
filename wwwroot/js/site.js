// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(() => {
    const currencyFormat = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const currencyTarget = input => {
        const targetId = input.dataset.currencyTarget;
        return targetId ? input.form?.querySelector(`#${CSS.escape(targetId)}`) || document.getElementById(targetId) : null;
    };
    const parseBrazilianAmount = value => {
        const normalized = (value ?? '').toString().trim().replace(/\./g, '').replace(',', '.');
        const amount = Number(normalized);
        return Number.isFinite(amount) ? amount : 0;
    };
    const syncCurrencyInput = input => {
        const target = currencyTarget(input);
        const digits = input.value.replace(/\D/g, '').slice(0, 11);
        if (!digits) {
            input.value = '';
            if (target) target.value = '';
            return;
        }
        const amount = Number(digits) / 100;
        input.value = currencyFormat.format(amount);
        if (target) target.value = amount.toFixed(2).replace('.', ',');
    };
    const initializeCurrencyMasks = root => {
        root.querySelectorAll('[data-currency-mask]').forEach(input => {
            if (input.dataset.currencyMaskReady === 'true') return;
            input.dataset.currencyMaskReady = 'true';
            const target = currencyTarget(input);
            const initial = target?.value || input.value;
            if (initial) {
                const cents = Math.round(parseBrazilianAmount(initial) * 100);
                input.value = currencyFormat.format(cents / 100);
                if (target) target.value = (cents / 100).toFixed(2).replace('.', ',');
            }
            input.addEventListener('input', () => syncCurrencyInput(input));
            input.form?.addEventListener('submit', () => syncCurrencyInput(input));
        });
    };
    initializeCurrencyMasks(document);

    document.addEventListener('click', event => {
        const openButton = event.target.closest('[data-app-modal-open]');
        if (openButton) {
            const dialog = document.querySelector(openButton.dataset.appModalOpen);
            const template = document.querySelector(openButton.dataset.appModalTemplate);
            const body = dialog?.querySelector('[data-app-modal-body]');
            if (!dialog || !template || !body) return;

            body.replaceChildren(template.content.cloneNode(true));
            initializeCurrencyMasks(body);
            dialog.showModal();
            requestAnimationFrame(() => body.querySelector('input:not([type="hidden"]), select, textarea')?.focus({ preventScroll: true }));
            return;
        }

        const closeButton = event.target.closest('[data-app-modal-close]');
        closeButton?.closest('dialog.app-modal')?.close();
    });

    document.addEventListener('click', event => {
        if (event.target instanceof HTMLDialogElement && event.target.matches('dialog.app-modal'))
            event.target.close();
    });

    const confirmDialog = document.getElementById('app-confirm-dialog');
    const confirmMessage = document.getElementById('app-confirm-message');
    const confirmTitle = document.getElementById('app-confirm-title');
    let pendingConfirmation = null;

    document.addEventListener('click', event => {
        const action = event.target.closest('[data-confirm-message]');
        if (!action || action.dataset.confirmBypass === 'true') return;

        event.preventDefault();
        pendingConfirmation = action;
        confirmMessage.textContent = action.dataset.confirmMessage || 'Deseja continuar com esta ação?';
        confirmTitle.textContent = action.dataset.confirmTitle || 'Confirmar ação';
        confirmDialog.showModal();
    });

    confirmDialog?.addEventListener('close', () => {
        const action = pendingConfirmation;
        pendingConfirmation = null;
        if (confirmDialog.returnValue !== 'confirm' || !action) {
            action?.focus({ preventScroll: true });
            return;
        }

        if (action instanceof HTMLAnchorElement) {
            window.location.assign(action.href);
            return;
        }

        const form = action.form;
        if (form) {
            if (typeof form.requestSubmit === 'function') form.requestSubmit(action);
            else form.submit();
            action.focus({ preventScroll: true });
            return;
        }

        action.dataset.confirmBypass = 'true';
        action.click();
        delete action.dataset.confirmBypass;
        action.focus({ preventScroll: true });
    });

    const normalize = value => (value ?? '').toString().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR').trim();
    let activeMenu = null;
    let menuSequence = 0;

    function createSearchMenu(anchor, options, onChoose, placeholder, allowCustom = false, positionAnchor = anchor, focusTarget = anchor) {
        const menu = document.createElement('div');
        menu.className = 'app-dropdown-menu';
        menu.id = `app-dropdown-menu-${++menuSequence}`;
        anchor.setAttribute('aria-controls', menu.id);
        menu.setAttribute('role', 'dialog');
        menu.setAttribute('aria-label', 'Pesquisar e selecionar uma opção');

        const searchWrap = document.createElement('div');
        searchWrap.className = 'app-dropdown-search-wrap';
        const search = document.createElement('input');
        search.type = 'search';
        search.className = 'app-dropdown-search';
        search.placeholder = placeholder || 'Pesquisar opções';
        search.setAttribute('aria-label', search.placeholder);
        search.autocomplete = 'off';
        searchWrap.append(search);

        const list = document.createElement('div');
        list.className = 'app-dropdown-options';
        list.setAttribute('role', 'listbox');
        menu.append(searchWrap, list);

        const render = () => {
            const query = normalize(search.value);
            list.replaceChildren();
            const visible = options().filter(option => !option.disabled && (!query || normalize(option.text).includes(query)));
            for (const option of visible) {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'app-dropdown-option';
                item.setAttribute('role', 'option');
                item.textContent = option.text;
                if (option.selected) item.setAttribute('aria-selected', 'true');
                item.addEventListener('click', () => {
                    onChoose(option);
                    closeMenu();
                    focusTarget.focus({ preventScroll: true });
                });
                list.append(item);
            }
            if (!visible.length) {
                const empty = document.createElement('div');
                empty.className = 'app-dropdown-empty';
                empty.textContent = query && allowCustom ? 'Use o texto digitado ou escolha uma opção' : 'Nenhuma opção encontrada';
                list.append(empty);
            }
            if (menu.isConnected) position();
        };

        function position() {
            if (!menu.isConnected) return;
            const rect = positionAnchor.getBoundingClientRect();
            const margin = 8;
            const availableBelow = window.innerHeight - rect.bottom - margin;
            const availableAbove = rect.top - margin;
            const openAbove = availableBelow < 210 && availableAbove > availableBelow;
            const available = Math.max(120, Math.min(300, openAbove ? availableAbove : availableBelow));
            menu.style.maxHeight = `${available}px`;
            const menuHeight = Math.min(available, menu.scrollHeight);
            menu.style.left = `${Math.max(margin, Math.min(rect.left, window.innerWidth - rect.width - margin))}px`;
            menu.style.width = `${Math.min(rect.width, window.innerWidth - margin * 2)}px`;
            menu.style.top = openAbove ? `${Math.max(margin, rect.top - menuHeight - 4)}px` : `${rect.bottom + 4}px`;
        }

        function closeMenu() {
            menu.remove();
            anchor.setAttribute('aria-expanded', 'false');
            if (activeMenu?.menu === menu) activeMenu = null;
            window.removeEventListener('resize', position);
            window.removeEventListener('scroll', position, true);
        }

        function openMenu(focusSearch = true, query = '') {
            if (activeMenu?.menu === menu) {
                search.value = query;
                render();
                if (focusSearch) search.focus({ preventScroll: true });
                position();
                return;
            }
            activeMenu?.close();
            document.body.append(menu);
            anchor.setAttribute('aria-expanded', 'true');
            search.value = query;
            render();
            position();
            if (focusSearch) search.focus({ preventScroll: true });
            activeMenu = { menu, close: closeMenu };
            window.addEventListener('resize', position);
            window.addEventListener('scroll', position, true);
        }

        search.addEventListener('input', render);
        search.addEventListener('keydown', event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                closeMenu();
                focusTarget.focus();
            } else if (event.key === 'ArrowDown') {
                event.preventDefault();
                list.querySelector('.app-dropdown-option')?.focus();
            } else if (event.key === 'Enter' && allowCustom && search.value.trim()) {
                event.preventDefault();
                onChoose({ value: search.value.trim(), text: search.value.trim() });
                closeMenu();
                focusTarget.focus({ preventScroll: true });
            } else if (event.key === 'Enter') {
                const matches = list.querySelectorAll('.app-dropdown-option');
                if (matches.length === 1) {
                    event.preventDefault();
                    matches[0].click();
                } else if (matches.length > 1) {
                    event.preventDefault();
                    matches[0].focus();
                }
            }
        });
        menu.addEventListener('keydown', event => {
            const items = [...list.querySelectorAll('.app-dropdown-option')];
            const index = items.indexOf(document.activeElement);
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                items[Math.max(0, Math.min(items.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)))]?.focus();
            } else if (event.key === 'Escape') {
                event.preventDefault();
                closeMenu();
                focusTarget.focus();
            }
        });

        return { open: openMenu, close: closeMenu, render };
    }

    function enhanceSelect(select) {
        if (select.dataset.appSelectReady === 'true') return;
        select.dataset.appSelectReady = 'true';

        const wrapper = document.createElement('div');
        wrapper.className = `app-select-control${select.classList.contains('form-select-sm') ? ' app-select-small' : ''}`;
        select.parentNode.insertBefore(wrapper, select);
        wrapper.append(select);

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'app-select-trigger';
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-required', select.required ? 'true' : 'false');
        if (select.id) trigger.id = `${select.id}__combobox`;
        const label = select.id ? document.querySelector(`label[for="${CSS.escape(select.id)}"]`) : null;
        const nearbyLabel = label || wrapper.parentElement.querySelector(':scope > label.form-label') || select.closest('.process-filter')?.querySelector('label');
        if (nearbyLabel) {
            const label = nearbyLabel;
            if (select.id) label.htmlFor = trigger.id;
            trigger.setAttribute('aria-labelledby', label.id || (label.id = `${trigger.id}__label`));
        } else if (select.getAttribute('aria-label')) {
            trigger.setAttribute('aria-label', select.getAttribute('aria-label'));
        } else {
            trigger.setAttribute('aria-label', select.options[0]?.textContent?.trim() || 'Selecionar opção');
        }
        const valueText = document.createElement('span');
        valueText.className = 'app-select-value';
        const chevron = document.createElement('span');
        chevron.className = 'app-select-chevron';
        chevron.setAttribute('aria-hidden', 'true');
        trigger.append(valueText, chevron);
        wrapper.append(trigger);

        select.classList.add('app-select-native');
        select.tabIndex = -1;
        select.setAttribute('aria-hidden', 'true');

        const sync = () => {
            const selected = select.options[select.selectedIndex];
            valueText.textContent = selected?.textContent?.trim() || 'Selecione';
            valueText.classList.toggle('app-select-placeholder', !selected || selected.value === '');
            trigger.disabled = select.disabled;
            trigger.classList.toggle('is-invalid', select.classList.contains('input-validation-error'));
        };
        const menu = createSearchMenu(trigger, () => [...select.options].map(option => ({
            value: option.value,
            text: option.textContent.trim(),
            selected: option.selected,
            disabled: option.disabled || option.hidden
        })), option => {
            select.value = option.value;
            select.dispatchEvent(new Event('input', { bubbles: true }));
            select.dispatchEvent(new Event('change', { bubbles: true }));
            sync();
        }, 'Pesquisar opções');

        trigger.addEventListener('click', () => menu.open());
        trigger.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                menu.open();
            }
        });
        select.addEventListener('change', sync);
        select.addEventListener('invalid', event => {
            event.preventDefault();
            trigger.classList.add('is-invalid');
            trigger.focus();
            menu.open();
        });
        select.form?.addEventListener('reset', () => window.setTimeout(sync, 0));
        new MutationObserver(() => {
            sync();
            menu.render();
        }).observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'disabled', 'hidden', 'selected'] });
        sync();
    }

    function enhanceTextCombobox(input, options) {
        if (input.dataset.appComboboxReady === 'true') return;
        input.dataset.appComboboxReady = 'true';
        const wrapper = document.createElement('div');
        wrapper.className = 'app-combobox-control';
        input.parentNode.insertBefore(wrapper, input);
        wrapper.append(input);

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'app-combobox-toggle';
        toggle.setAttribute('aria-label', 'Mostrar opções');
        toggle.setAttribute('aria-haspopup', 'listbox');
        toggle.setAttribute('aria-expanded', 'false');
        toggle.innerHTML = '<span class="app-select-chevron" aria-hidden="true"></span>';
        wrapper.append(toggle);

        const menu = createSearchMenu(toggle, () => options.map(text => ({ value: text, text })), option => {
            input.value = option.value;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }, 'Pesquisar comarcas', true, wrapper, input);
        toggle.addEventListener('click', () => menu.open());
        input.addEventListener('input', () => menu.open(false, input.value));
        input.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                menu.open(false, input.value);
                activeMenu?.menu.querySelector('.app-dropdown-option')?.focus();
            } else if (event.key === 'Escape') {
                menu.close();
            }
        });
    }

    document.querySelectorAll('select.form-select').forEach(enhanceSelect);
    document.querySelectorAll('form[data-catalogo-foros] input[data-searchable-combobox]').forEach(input => {
        try {
            const catalog = JSON.parse(input.form.dataset.catalogoForos || '{}');
            enhanceTextCombobox(input, [...new Set(Object.values(catalog))].sort((a, b) => a.localeCompare(b, 'pt-BR')));
        } catch {
            enhanceTextCombobox(input, []);
        }
    });

    document.addEventListener('click', event => {
        if (activeMenu && !activeMenu.menu.contains(event.target) && !event.target.closest('.app-select-trigger, .app-combobox-toggle, .app-combobox-control'))
            activeMenu.close();
    });
})();
