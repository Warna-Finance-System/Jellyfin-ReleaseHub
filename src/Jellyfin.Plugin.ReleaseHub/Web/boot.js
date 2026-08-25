/*
 * ReleaseHub bootstrap.
 *
 * Injected into jellyfin-web's index.html by the File Transformation plugin when it is present.
 *
 * Jellyfin 10.11 puts every plugin page behind an admin route guard and renders it inside the server
 * dashboard layout, complete with the administration sidebar. That is the wrong place for a viewer
 * feature, so ReleaseHub renders its own full-page view inside the ordinary user layout instead: the
 * Jellyfin header and drawer stay exactly as they are, and no dashboard chrome appears.
 *
 * Three entry points lead to it, in decreasing order of visibility:
 *   - a tab beside Home / Favorites in the header tab bar
 *   - an entry in the main drawer
 *   - Dashboard > Plugins > ReleaseHub, which still works without this file
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
  var PAGE_ID = 'releaseHubUserPage';
  var CSS_ID = 'releasehub-css';
  var TITLE = 'ReleaseHub';
  var CONFIG_PAGE = 'ReleaseHub';

  var isOpen = false;
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

    markTabActive(true);
    document.body.classList.add('releasehub-open');

    if (pushHistory !== false) {
      // Gives the browser's back button something to pop, so leaving ReleaseHub feels native.
      try {
        window.history.pushState({ releaseHub: true }, '');
      } catch (error) {
        /* history is unavailable in some embedded clients; the drawer still navigates away */
      }
    }

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

    var page = document.getElementById(PAGE_ID);
    if (page) {
      page.remove();
    }

    var target = hosts();
    if (target.routed) {
      target.routed.style.display = '';
    }

    markTabActive(false);
    document.body.classList.remove('releasehub-open');
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
      healStuckState();
      patchDashboardIcon();
      addDrawerEntry();
      addHeaderTab();
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
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
  } else {
    start();
  }
})();
