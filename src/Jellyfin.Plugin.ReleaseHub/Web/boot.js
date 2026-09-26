/*
 * ReleaseHub bootstrap.
 *
 * Injected into jellyfin-web's index.html by the File Transformation plugin when it is present.
 *
 * Jellyfin (10.11 and 12 alike) puts every plugin page behind an admin route guard and renders it
 * inside the server dashboard layout, complete with the administration sidebar. That is the wrong
 * place for a viewer feature, so ReleaseHub renders its own full-page view inside the ordinary user
 * layout instead: the Jellyfin header and drawer stay exactly as they are, and no dashboard chrome
 * appears.
 *
 * jellyfin-web ships two layouts, and the entry points differ between them:
 *
 *   Legacy layout (the default up to Jellyfin 10.11; since 12, the `desktop-legacy`,
 *   `mobile-legacy` and `tv` layouts)
 *     - a tab beside Home / Favorites in the header tab bar
 *     - an entry in the main drawer
 *
 *   Modern layout (the default for desktop and mobile since Jellyfin 12; "experimental" in 10.11)
 *     - a button at the end of the header navigation, after the libraries
 *     - an entry in the navigation drawer that replaces it on narrow screens
 *
 * and in both, Dashboard > Plugins > ReleaseHub, which still works without this file.
 *
 * Everything here is optional and defensive: without File Transformation this file is never loaded,
 * and if jellyfin-web changes its markup each enhancement simply stops applying rather than breaking
 * navigation.
 */
(function () {
  'use strict';

  if (window.__releaseHubBootstrapped) {
    return;
  }
  window.__releaseHubBootstrapped = true;

  var TAB_ID = 'releaseHubHeaderTab';
  var DRAWER_ID = 'releaseHubDrawerLink';
  var NAV_BUTTON_ID = 'releaseHubNavButton';
  var NAV_DRAWER_ID = 'releaseHubNavDrawerItem';
  var PAGE_ID = 'releaseHubUserPage';
  var CSS_ID = 'releasehub-css';
  var TITLE = 'ReleaseHub';
  var CONFIG_PAGE = 'ReleaseHub';

  // Marks every element that opens ReleaseHub, so the navigation watcher never mistakes one for a
  // link leading away from it.
  var ENTRY_ATTRIBUTE = 'data-releasehub-entry';
  var NAV_ACTIVE_CLASS = 'releasehub-nav-active';
  var NAV_DIMMED_CLASS = 'releasehub-nav-dimmed';
  var NAV_LABEL_CLASS = 'releasehub-nav-label';

  // The Favorites link is the one element the modern header and its drawer always render, whatever
  // libraries the user can see, so it is what both are located by.
  var FAVORITES_LINK = 'a[href$="/home?tab=1"]';

  // The CalendarMonth glyph exactly as @mui/icons-material draws it, so ReleaseHub's icon matches the
  // library icons around it in size and weight.
  var SVG_NS = 'http://www.w3.org/2000/svg';
  var CALENDAR_PATH = 'M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 '
    + '2-.9 2-2V6c0-1.1-.9-2-2-2m0 16H5V10h14zM9 14H7v-2h2zm4 0h-2v-2h2zm4 0h-2v-2h2zm-8 4H7v-2h2zm4 '
    + '0h-2v-2h2zm4 0h-2v-2h2z';

  var isOpen = false;
  var openedAt = null;
  var appLoading = null;

  /* ----------------------------------------------------------- asset loading */

  function assetUrl(name) {
    return window.ApiClient
      ? ApiClient.getUrl('web/ConfigurationPage', { name: name })
      : '/web/ConfigurationPage?name=' + encodeURIComponent(name);
  }

  function ensureStylesheet() {
    if (document.getElementById(CSS_ID)) {
      return;
    }

    var link = document.createElement('link');
    link.id = CSS_ID;
    link.rel = 'stylesheet';
    link.href = assetUrl('releasehub.css');
    document.head.appendChild(link);
  }

  function ensureApp() {
    if (window.ReleaseHub && window.ReleaseHub.mount) {
      return Promise.resolve();
    }

    // Cached so that clicking the tab repeatedly cannot start several downloads.
    if (!appLoading) {
      appLoading = new Promise(function (resolve, reject) {
        var script = document.createElement('script');
        script.src = assetUrl('releasehub-app.js');
        script.onload = resolve;
        script.onerror = function () {
          appLoading = null;
          reject(new Error('ReleaseHub application script failed to load'));
        };
        document.head.appendChild(script);
      });
    }

    return appLoading;
  }

  /* ------------------------------------------------------- full-page view */

  function hosts() {
    return {
      // Jellyfin's own legacy view loader appends into .mainAnimatedPages, so adding a child there is
      // safe from React reconciliation, unlike the sibling that holds the routed views.
      view: document.querySelector('.mainAnimatedPages'),
      routed: document.querySelector('.skinBody:not(.mainAnimatedPages)')
    };
  }

  /**
   * Reads an opaque background colour out of the active theme.
   *
   * ReleaseHub paints over the page it covers, because the underlying home view stays mounted and
   * other plugins (a backdrop slideshow, for instance) draw on top of ordinary content. Sampling the
   * theme rather than hardcoding a colour keeps that working on dark, light and third-party themes
   * alike.
   */
  /** Whether a computed colour is opaque enough to paint over a backdrop. */
  function isOpaque(colour) {
    if (!colour || colour === 'transparent') {
      return false;
    }

    var match = colour.match(/rgba?\(([^)]+)\)/);
    if (!match) {
      return false;
    }

    var parts = match[1].split(',');
    // No alpha component means fully opaque; otherwise require near-full opacity.
    return parts.length < 4 || parseFloat(parts[3]) > 0.95;
  }

  /**
   * Reads an opaque background colour out of the active theme.
   *
   * ReleaseHub paints over the page it covers: the home view stays mounted underneath, and plugins
   * such as a backdrop slideshow draw their own full-screen layers. Anything less than a fully opaque
   * background lets them show through.
   *
   * The candidates are ordered from the most reliably solid surface downwards. `.mainDrawer` and
   * `.dialog` are panels every theme paints; `.backgroundContainer` is listed too but is frequently
   * transparent precisely when a backdrop is active, which is exactly when we need a colour.
   */
  function themeBackground() {
    var candidates = [
      '.mainDrawer',
      '.dialog',
      '.skinHeader-withBackground',
      '.backgroundContainer',
      'body',
      'html'
    ];

    for (var i = 0; i < candidates.length; i++) {
      var node = document.querySelector(candidates[i]);
      if (!node) {
        continue;
      }

      var colour = window.getComputedStyle(node).backgroundColor;
      if (isOpaque(colour)) {
        return colour;
      }
    }

    // The modern layout paints its surfaces with MUI, whose theme jellyfin-web publishes as CSS
    // variables (prefix `jf`). None of the legacy surfaces above exist there to be sampled.
    var muiBackground = window.getComputedStyle(document.documentElement)
      .getPropertyValue('--jf-palette-background-default')
      .trim();
    if (muiBackground) {
      return muiBackground;
    }

    // Last resort: derive one from the text colour's luminance rather than hardcoding a theme's
    // palette. A light-on-dark theme yields a near-black ground, a dark-on-light one a near-white.
    var text = window.getComputedStyle(document.body).color;
    var rgb = (text.match(/rgba?\(([^)]+)\)/) || [])[1];

    if (rgb) {
      var v = rgb.split(',').map(parseFloat);
      var luminance = (0.299 * v[0]) + (0.587 * v[1]) + (0.114 * v[2]);
      return luminance > 128 ? 'rgb(16, 16, 16)' : 'rgb(245, 245, 245)';
    }

    return 'rgb(16, 16, 16)';
  }

  function openReleaseHub(pushHistory) {
    var target = hosts();
    if (!target.view || isOpen) {
      return;
    }

    isOpen = true;
    ensureStylesheet();

    if (target.routed) {
      target.routed.style.display = 'none';
    }

    var page = document.createElement('div');
    page.id = PAGE_ID;
    page.className = 'mainAnimatedPage releasehub-page';

    var background = themeBackground();
    if (background) {
      page.style.backgroundColor = background;
    }

    var root = document.createElement('div');
    root.className = 'releasehub-root';
    page.appendChild(root);
    target.view.appendChild(page);

    markEntriesActive(true);
    document.body.classList.add('releasehub-open');

    if (pushHistory !== false) {
      // Gives the browser's back button something to pop, so leaving ReleaseHub feels native.
      try {
        window.history.pushState({ releaseHub: true }, '');
      } catch (error) {
        /* history is unavailable in some embedded clients; the drawer still navigates away */
      }
    }

    // Read after the push above, which keeps the URL unchanged: any later difference means the user
    // went somewhere else.
    openedAt = window.location.href;

    ensureApp().then(function () {
      if (isOpen && document.getElementById(PAGE_ID)) {
        window.ReleaseHub.mount(root);
      }
    }, function (error) {
      root.textContent = 'ReleaseHub could not be loaded.';
      console.error('[ReleaseHub]', error);
    });
  }

  function closeReleaseHub() {
    if (!isOpen) {
      return;
    }

    isOpen = false;
    openedAt = null;

    var page = document.getElementById(PAGE_ID);
    if (page) {
      page.remove();
    }

    var target = hosts();
    if (target.routed) {
      target.routed.style.display = '';
    }

    markEntriesActive(false);
    document.body.classList.remove('releasehub-open');
  }

  function markEntriesActive(active) {
    markTabActive(active);
    markNavActive(active);
  }

  /* ------------------------------------------------- header tab (Home area) */

  function markTabActive(active) {
    var tab = document.getElementById(TAB_ID);
    if (!tab) {
      return;
    }

    tab.classList.toggle('emby-tab-button-active', active);

    // While ReleaseHub is showing, Jellyfin's own tab still thinks it is selected; clearing it keeps
    // exactly one tab looking active.
    var siblings = tab.parentNode ? tab.parentNode.children : [];
    for (var i = 0; i < siblings.length; i++) {
      if (siblings[i] !== tab && active) {
        siblings[i].classList.remove('emby-tab-button-active');
      }
    }
  }

  function addHeaderTab() {
    var slider = document.querySelector('.headerTabs .emby-tabs-slider');
    if (!slider || document.getElementById(TAB_ID)) {
      return;
    }

    // Only alongside the Home/Favorites tabs; other views own their own tab bar.
    if (!document.querySelector('.homePage, #indexPage')) {
      return;
    }

    // A plain button, deliberately not a real Jellyfin tab: the home view's tab list is a fixed
    // two-entry array and its controller loader is a switch on the index, so a third participating
    // tab would throw. This one just opens ReleaseHub.
    var tab = document.createElement('button');
    tab.id = TAB_ID;
    tab.type = 'button';
    tab.setAttribute('is', 'emby-button');
    tab.className = 'emby-tab-button emby-button';

    var label = document.createElement('div');
    label.className = 'emby-button-foreground';
    label.textContent = TITLE;
    tab.appendChild(label);

    tab.addEventListener('click', function (event) {
      event.preventDefault();
      event.stopPropagation();
      openReleaseHub();
    });

    slider.appendChild(tab);

    // Home and Favorites switch tabs without changing the hash, so navigating away from ReleaseHub
    // produces no route event at all. Without this the overlay would stay on top of the tab the user
    // just asked for, which is exactly what made Favorites look empty.
    var siblings = slider.children;
    for (var i = 0; i < siblings.length; i++) {
      var sibling = siblings[i];
      if (sibling !== tab && !sibling.hasAttribute('data-releasehub-bound')) {
        sibling.setAttribute('data-releasehub-bound', '1');
        sibling.addEventListener('click', function () {
          closeReleaseHub();
        });
      }
    }

    if (isOpen) {
      markTabActive(true);
    }
  }

  /* -------------------------------------------------------- drawer entry */

  function addDrawerEntry() {
    var drawer = document.querySelector('.mainDrawer');
    if (!drawer || document.getElementById(DRAWER_ID)) {
      return;
    }

    var sibling = drawer.querySelector('.navMenuOption');
    if (!sibling || !sibling.parentNode) {
      return;
    }

    // Cloning a native entry's classes means themes style it with no extra rules from us.
    var link = document.createElement('a');
    link.id = DRAWER_ID;
    link.setAttribute('is', 'emby-linkbutton');
    link.setAttribute(ENTRY_ATTRIBUTE, '1');
    link.className = sibling.className;
    link.href = '#';

    var glyph = document.createElement('span');
    glyph.className = 'material-icons navMenuOptionIcon';
    glyph.setAttribute('aria-hidden', 'true');
    glyph.textContent = 'calendar_month';

    var text = document.createElement('span');
    text.className = 'navMenuOptionText';
    text.textContent = TITLE;

    link.appendChild(glyph);
    link.appendChild(text);

    link.addEventListener('click', function (event) {
      event.preventDefault();

      // Close the drawer the way Jellyfin's own entries do, then open the view.
      var backdrop = document.querySelector('.drawer-open ~ .backdrop, .dialogBackdropOpened');
      if (backdrop) {
        backdrop.click();
      } else {
        var button = document.querySelector('.mainDrawerButton');
        if (button && document.querySelector('.drawer-open')) {
          button.click();
        }
      }

      openReleaseHub();
    });

    sibling.parentNode.insertBefore(link, sibling.nextSibling);
  }

  /* ------------------------------------------ modern layout (MUI header) */

  /*
   * The modern header is React. Its navigation is a row of MUI buttons — the server name, Favorites,
   * then one per library, then "More" when they do not fit — and on narrow screens a drawer replaces
   * the row. ReleaseHub cannot ask React for another entry, so it copies a native one: same element,
   * same classes, so every theme (Abyss included) styles it exactly like its neighbours, with no
   * theme-specific rule here.
   *
   * React leaves a node it does not own alone, but it appends buttons of its own as the libraries
   * load, which can land after ours. apply() runs on every mutation and moves ReleaseHub back to the
   * end, which is a no-op once it is there.
   */

  function swapClass(node, from, to) {
    if (node.classList.contains(from)) {
      node.classList.remove(from);
      node.classList.add(to);
    }
  }

  function calendarIcon(model) {
    // A shallow copy of the neighbour's icon keeps the classes that size it; only the glyph changes.
    var svg = model ? model.cloneNode(false) : document.createElementNS(SVG_NS, 'svg');

    if (!model) {
      svg.setAttribute('class', 'MuiSvgIcon-root MuiSvgIcon-fontSizeMedium');
      svg.style.width = '1em';
      svg.style.height = '1em';
      svg.style.fill = 'currentColor';
      svg.style.flexShrink = '0';
    }

    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('focusable', 'false');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('data-testid', 'CalendarMonthIcon');

    var path = document.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', CALENDAR_PATH);
    svg.appendChild(path);

    return svg;
  }

  function setCalendarIcon(node) {
    var existing = node.querySelector('svg');
    if (existing) {
      existing.parentNode.replaceChild(calendarIcon(existing), existing);
      return;
    }

    // A library icon drawn some other way (an icon font, an image): replace whatever is there.
    var holder = node.querySelector('.MuiButton-startIcon, .MuiListItemIcon-root');
    if (holder) {
      while (holder.firstChild) {
        holder.removeChild(holder.firstChild);
      }

      holder.appendChild(calendarIcon(null));
    }
  }

  /** Makes a copy of a native entry inert: no route, no leftover state from the original. */
  function prepareCopy(node) {
    node.setAttribute('href', '#');
    node.setAttribute('role', 'button');
    node.setAttribute(ENTRY_ATTRIBUTE, '1');
    node.removeAttribute('aria-current');
    node.classList.remove('Mui-selected', 'Mui-focusVisible');
  }

  /** The row of buttons in the modern header, or null when this is not the modern layout. */
  function modernNav() {
    var links = document.querySelectorAll('.MuiToolbar-root ' + FAVORITES_LINK);

    for (var i = 0; i < links.length; i++) {
      var row = links[i].parentElement;
      if (row && row.classList.contains('MuiStack-root')) {
        return row;
      }
    }

    return null;
  }

  /** Picks the native button ReleaseHub is copied from: a library, not the larger server name. */
  function navTemplate(nav) {
    var fallback = null;

    for (var i = 0; i < nav.children.length; i++) {
      var candidate = nav.children[i];

      if (candidate.id === NAV_BUTTON_ID
        || candidate.tagName !== 'A'
        || !candidate.classList.contains('MuiButton-root')
        || candidate.classList.contains('MuiButton-sizeLarge')) {
        continue;
      }

      // An inactive button carries the neutral colour ReleaseHub should start with.
      if (candidate.classList.contains('MuiButton-colorInherit')) {
        return candidate;
      }

      fallback = fallback || candidate;
    }

    return fallback;
  }

  function buildNavButton(template) {
    var button = template.cloneNode(true);
    button.id = NAV_BUTTON_ID;
    prepareCopy(button);
    swapClass(button, 'MuiButton-colorPrimary', 'MuiButton-colorInherit');
    swapClass(button, 'MuiButton-textPrimary', 'MuiButton-textInherit');
    setCalendarIcon(button);

    // The copied label is the button's bare text; the icon and the ripple are the element children.
    var textNodes = [];
    for (var i = 0; i < button.childNodes.length; i++) {
      if (button.childNodes[i].nodeType === Node.TEXT_NODE) {
        textNodes.push(button.childNodes[i]);
      }
    }

    textNodes.forEach(function (node) {
      node.remove();
    });

    // Ours goes in a span of its own so that fitNav() can hide it when the row runs short.
    var label = document.createElement('span');
    label.className = NAV_LABEL_CLASS;
    label.textContent = TITLE;

    var icon = button.querySelector('.MuiButton-startIcon');
    var anchor = icon && icon.parentNode === button
      ? icon.nextSibling
      : button.querySelector('.MuiTouchRipple-root');
    button.insertBefore(label, anchor || null);

    button.addEventListener('click', function (event) {
      event.preventDefault();
      openReleaseHub();
    });

    return button;
  }

  function addNavButton() {
    var nav = modernNav();
    if (!nav) {
      return;
    }

    var button = document.getElementById(NAV_BUTTON_ID);

    if (!button) {
      var template = navTemplate(nav);
      if (!template) {
        return;
      }

      button = buildNavButton(template);
    }

    // Last, after every library and after "More": where the user looks for something that is not a
    // library, and where React's own additions cannot push it into the middle of the row.
    if (button.parentNode !== nav || nav.lastElementChild !== button) {
      nav.appendChild(button);
      scheduleNavFit();
    }

    watchNav(nav);

    if (isOpen && !button.classList.contains(NAV_ACTIVE_CLASS)) {
      markNavActive(true);
    }
  }

  /* ------------------------------------------------ fitting the header row */

  /*
   * One more entry can be one too many for the row. Themes lay it out differently — Abyss centres it
   * in an absolutely positioned box capped at a share of the toolbar — but every one fails the same
   * way when it runs short: the longest label, usually the server name, breaks over several lines
   * and the whole bar grows to match. ReleaseHub must never be what causes that.
   *
   * So the arrangements are tried from the most generous down, keeping the first in which the header
   * holds on one line and the row stays clear of every control around it:
   *   1. the row kept on one line, ReleaseHub with its label;
   *   2. the row kept on one line, ReleaseHub as an icon, named by its tooltip;
   *   3. the theme's own layout with ReleaseHub as an icon: when nothing fits, taking as little room
   *      as possible is all ReleaseHub can do.
   * Each attempt is measured and settled within one frame, before anything is painted, and always
   * from the top, so a wider window brings the label back.
   */

  var fitPending = false;
  var observedNav = null;
  var navObserver = window.ResizeObserver ? new window.ResizeObserver(scheduleNavFit) : null;

  function scheduleNavFit() {
    if (fitPending) {
      return;
    }

    fitPending = true;
    window.requestAnimationFrame(function () {
      fitPending = false;
      try {
        fitNav();
      } catch (error) {
        console.debug('[ReleaseHub] header fitting skipped:', error);
      }
    });
  }

  /** Re-fits whenever the row or the toolbar changes size: resizing, fonts, libraries loading. */
  function watchNav(nav) {
    if (!navObserver || observedNav === nav) {
      return;
    }

    navObserver.disconnect();
    observedNav = nav;
    navObserver.observe(nav);
    if (nav.parentElement) {
      navObserver.observe(nav.parentElement);
    }
  }

  function setNavCompact(button, compact) {
    var label = button.querySelector('.' + NAV_LABEL_CLASS);
    if (label) {
      label.style.display = compact ? 'none' : '';
    }

    // MUI offsets a start icon towards its label; with no label, that would leave it off centre.
    var icon = button.querySelector('.MuiButton-startIcon');
    if (icon) {
      icon.style.marginLeft = compact ? '0' : '';
      icon.style.marginRight = compact ? '0' : '';
    }

    if (compact) {
      button.setAttribute('aria-label', TITLE);
      button.setAttribute('title', TITLE);
    } else {
      button.removeAttribute('aria-label');
      button.removeAttribute('title');
    }
  }

  /** Whether any label inside the node is broken over more than one line. */
  function textWraps(node) {
    var range = document.createRange ? document.createRange() : null;
    if (!range || !range.getClientRects) {
      return false;
    }

    var walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT);
    for (var text = walker.nextNode(); text; text = walker.nextNode()) {
      if (!text.nodeValue.trim()) {
        continue;
      }

      range.selectNodeContents(text);
      var lines = range.getClientRects();
      for (var i = 1; i < lines.length; i++) {
        if (Math.abs(lines[i].top - lines[0].top) > 2) {
          return true;
        }
      }
    }

    return false;
  }

  function overlaps(a, b) {
    return a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1;
  }

  /** Whether the header holds on one line, with the row clear of every other control in it. */
  function navFits(nav) {
    var toolbar = nav.parentElement;
    if (!toolbar || textWraps(nav)) {
      return false;
    }

    var row = nav.getBoundingClientRect();
    var bar = toolbar.getBoundingClientRect();
    if (row.left < bar.left - 1 || row.right > bar.right + 1 || toolbar.scrollWidth > toolbar.clientWidth + 1) {
      return false;
    }

    // Compared with the controls themselves rather than their containers: a container stretched to
    // fill the toolbar sits behind a centred row without being covered by it.
    var middle = row.top + (row.height / 2);
    var controls = toolbar.querySelectorAll('a, button');

    for (var i = 0; i < controls.length; i++) {
      if (nav.contains(controls[i])) {
        continue;
      }

      var box = controls[i].getBoundingClientRect();
      if (!box.width || !box.height) {
        continue;
      }

      if (overlaps(row, box)) {
        return false;
      }

      // A control pushed down to a second line of the toolbar: the bar has grown just the same.
      if (Math.abs(box.top + (box.height / 2) - middle) > row.height) {
        return false;
      }
    }

    return true;
  }

  function fitNav() {
    var button = document.getElementById(NAV_BUTTON_ID);
    var nav = button && button.parentElement;
    if (!nav) {
      return;
    }

    // Inline, on an element React renders without a style prop, so React never resets it.
    nav.style.whiteSpace = 'nowrap';

    setNavCompact(button, false);
    if (navFits(nav)) {
      return;
    }

    setNavCompact(button, true);
    if (navFits(nav)) {
      return;
    }

    nav.style.whiteSpace = '';
  }

  /**
   * Shows ReleaseHub as the selected header entry while it is open.
   *
   * Only the MUI classes change. Themes key their highlight on them (Abyss paints
   * `.MuiButton-colorPrimary` with its accent), and releasehub.css covers the default theme, whose
   * colour otherwise lives in generated styles. The native button that was selected is dimmed the
   * same way and marked, and restored only if it still carries that mark: React rewrites the whole
   * class list when it re-renders a button, and its value is then the one to keep.
   */
  function markNavActive(active) {
    var button = document.getElementById(NAV_BUTTON_ID);

    if (button) {
      button.classList.toggle(NAV_ACTIVE_CLASS, active);
      swapClass(button, active ? 'MuiButton-colorInherit' : 'MuiButton-colorPrimary',
        active ? 'MuiButton-colorPrimary' : 'MuiButton-colorInherit');
      swapClass(button, active ? 'MuiButton-textInherit' : 'MuiButton-textPrimary',
        active ? 'MuiButton-textPrimary' : 'MuiButton-textInherit');

      if (active) {
        button.setAttribute('aria-current', 'page');
      } else {
        button.removeAttribute('aria-current');
      }
    }

    if (active) {
      var nav = button && button.parentNode;
      var selected = nav ? nav.querySelectorAll('.MuiButton-colorPrimary') : [];

      for (var i = 0; i < selected.length; i++) {
        if (selected[i] !== button) {
          swapClass(selected[i], 'MuiButton-colorPrimary', 'MuiButton-colorInherit');
          swapClass(selected[i], 'MuiButton-textPrimary', 'MuiButton-textInherit');
          selected[i].classList.add(NAV_DIMMED_CLASS);
        }
      }

      return;
    }

    var dimmed = document.querySelectorAll('.' + NAV_DIMMED_CLASS);
    for (var j = 0; j < dimmed.length; j++) {
      dimmed[j].classList.remove(NAV_DIMMED_CLASS);
      swapClass(dimmed[j], 'MuiButton-colorInherit', 'MuiButton-colorPrimary');
      swapClass(dimmed[j], 'MuiButton-textInherit', 'MuiButton-textPrimary');
    }
  }

  /**
   * Adds ReleaseHub below Favorites in the drawer that replaces the header row on narrow screens.
   *
   * No closing logic is needed: every click inside that drawer closes it, this one included.
   */
  function addNavDrawerItem() {
    if (document.getElementById(NAV_DRAWER_ID)) {
      return;
    }

    var favorites = document.querySelector('.MuiDrawer-paper ' + FAVORITES_LINK);
    var item = favorites && favorites.closest('li');
    if (!item || !item.parentNode) {
      return;
    }

    var entry = item.cloneNode(true);
    entry.id = NAV_DRAWER_ID;

    var link = entry.querySelector('a') || entry;
    prepareCopy(link);
    setCalendarIcon(link);

    var label = link.querySelector('.MuiListItemText-primary') || link.querySelector('.MuiListItemText-root');
    if (label) {
      label.textContent = TITLE;
    }

    link.addEventListener('click', function (event) {
      event.preventDefault();
      openReleaseHub();
    });

    item.parentNode.insertBefore(entry, item.nextSibling);
  }

  /* ---------------------------------------------------- leaving ReleaseHub */

  /**
   * Closes ReleaseHub once the user has gone somewhere else.
   *
   * The modern layout navigates with history.pushState, which fires neither `hashchange` nor
   * `popstate`, so the listeners in start() never hear about it: without this, picking a library in
   * the header would render it underneath ReleaseHub, out of sight. The URL is what changes, and the
   * new view rendering is what brings apply() here to notice.
   */
  function closeIfNavigatedAway() {
    if (isOpen && openedAt !== null && window.location.href !== openedAt) {
      closeReleaseHub();
    }
  }

  /**
   * Closes ReleaseHub when a link outside it is followed.
   *
   * Covers what closeIfNavigatedAway() cannot see: a link to the page already underneath, such as
   * Favorites while on Favorites, which leaves the URL as it was. Capturing runs before React
   * navigates, so the page being revealed is the one the link leads to.
   */
  function onDocumentClick(event) {
    if (!isOpen || !event.target || !event.target.closest) {
      return;
    }

    var link = event.target.closest('a[href]');
    if (!link
      || link.target === '_blank'
      || link.hasAttribute(ENTRY_ATTRIBUTE)
      || link.closest('#' + PAGE_ID)) {
      return;
    }

    closeReleaseHub();
  }

  /* ------------------------------------------------------------ scheduling */

  /**
   * Undoes the open state if our page has disappeared without closeReleaseHub() having run.
   *
   * Jellyfin's router navigates with history.pushState, which fires neither `hashchange` nor
   * `popstate`, so leaving ReleaseHub by some routes never reaches our listeners. The page node is
   * then discarded by Jellyfin's view manager while `body.releasehub-open` — and its
   * `overflow: hidden` — stays behind, freezing scrolling across the whole web UI.
   *
   * Rather than trying to enumerate every way Jellyfin can navigate, this observes the outcome: the
   * flag is only legitimate while our page is actually in the document.
   */
  function healStuckState() {
    if (!document.body.classList.contains('releasehub-open')) {
      return;
    }

    if (document.getElementById(PAGE_ID)) {
      return;
    }

    isOpen = false;
    document.body.classList.remove('releasehub-open');

    var routed = hosts().routed;
    if (routed) {
      routed.style.display = '';
    }
  }

  /* ------------------------------------------- dashboard drawer icon (admin) */

  /**
   * Replaces the generic plugin glyph beside ReleaseHub in the dashboard's left menu with a calendar.
   *
   * jellyfin-web 10.11 renders one hardcoded icon component for every plugin entry and ignores
   * PluginPageInfo.MenuIcon entirely, so there is no server-side way to influence this. Purely
   * cosmetic: if the markup ever changes, the entry simply keeps Jellyfin's default icon.
   */
  function patchDashboardIcon() {
    var links = document.querySelectorAll('a[href*="configurationpage"]');

    for (var i = 0; i < links.length; i++) {
      var link = links[i];

      if (link.getAttribute('data-releasehub-icon') === 'done') {
        continue;
      }

      if ((link.getAttribute('href') || '').indexOf('name=' + CONFIG_PAGE) === -1) {
        continue;
      }

      var holder = link.querySelector('[class*="MuiListItemIcon-root"]') || link;
      var existing = holder.querySelector('svg');
      if (!existing) {
        continue;
      }

      var glyph = document.createElement('span');
      glyph.className = 'material-icons';
      glyph.setAttribute('aria-hidden', 'true');
      glyph.textContent = 'calendar_month';
      glyph.style.fontSize = '1.5rem';

      existing.replaceWith(glyph);
      link.setAttribute('data-releasehub-icon', 'done');
    }
  }

  function apply() {
    try {
      closeIfNavigatedAway();
      healStuckState();
      patchDashboardIcon();

      // Each pair finds nothing to attach to in the other layout, so both run unconditionally and
      // switching layout in the display settings needs no reload.
      addDrawerEntry();
      addHeaderTab();
      addNavButton();
      addNavDrawerItem();
    } catch (error) {
      console.debug('[ReleaseHub] UI enhancement skipped:', error);
    }
  }

  function start() {
    apply();

    // The header and drawer are rendered lazily and rebuilt on view changes, so watch rather than
    // poll. The observer only reacts to nodes being added.
    new MutationObserver(apply).observe(document.body, { childList: true, subtree: true });

    // Navigating anywhere else must dismiss our view; it is not a routed page.
    window.addEventListener('hashchange', function () {
      closeReleaseHub();
      apply();
    });

    window.addEventListener('popstate', function () {
      closeReleaseHub();
    });

    document.addEventListener('click', onDocumentClick, true);

    // The resize observer covers this where it exists; this covers the browsers without one.
    window.addEventListener('resize', scheduleNavFit);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
  } else {
    start();
  }
})();
