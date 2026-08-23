/*
 * ReleaseHub application.
 *
 * Loaded on demand by Web/app.html; exposes window.ReleaseHub.mount(container).
 *
 * Two rules drive most of the rendering decisions below:
 *   1. Never invent a date or a time. The API distinguishes an exact broadcast from an approximate or
 *      merely announced one, and a date that carries a real airtime from one that does not. Each case
 *      is rendered differently rather than being flattened into a timestamp.
 *   2. Never hardcode a colour. Jellyfin themes (dark, light, Abyss, ...) restyle Jellyfin's own CSS
 *      classes, so the markup reuses those classes and the stylesheet sticks to currentColor.
 */
(function () {
  'use strict';

  var API = 'ReleaseHub';
  var SUPPORTED_CULTURES = ['en-US', 'fr-FR'];

  var LAYOUT_STORAGE_KEY = 'releasehub.layout';

  var state = {
    view: 'calendar',
    filter: 'all',
    range: 7,
    layout: 'list',
    weekOffset: 0,
    query: '',
    strings: {},
    status: null,
    container: null
  };

  function loadLayoutPreference() {
    // Per-browser convenience only; wrapped because storage throws outright in some privacy modes.
    try {
      var stored = window.localStorage.getItem(LAYOUT_STORAGE_KEY);
      if (stored === 'list' || stored === 'week' || stored === 'poster') {
        state.layout = stored;
      }
    } catch (error) {
      /* keep the default */
    }
  }

  function saveLayoutPreference(layout) {
    try {
      window.localStorage.setItem(LAYOUT_STORAGE_KEY, layout);
    } catch (error) {
      /* not worth surfacing: the choice simply will not persist */
    }
  }

  /* ---------------------------------------------------------------- i18n */

  function resolveCulture(configured) {
    // An administrator override wins; otherwise follow the client, exactly as jellyfin-web itself
    // resolves a locale (data-culture first, then the browser's preference).
    var requested = configured
      || document.documentElement.getAttribute('data-culture')
      || navigator.language
      || 'en-US';

    var i;
    for (i = 0; i < SUPPORTED_CULTURES.length; i++) {
      if (SUPPORTED_CULTURES[i].toLowerCase() === requested.toLowerCase()) {
        return SUPPORTED_CULTURES[i];
      }
    }

    // fr-CA, fr, fr-BE ... all resolve to the French we ship.
    var language = requested.split('-')[0].toLowerCase();
    for (i = 0; i < SUPPORTED_CULTURES.length; i++) {
      if (SUPPORTED_CULTURES[i].split('-')[0].toLowerCase() === language) {
        return SUPPORTED_CULTURES[i];
      }
    }

    return 'en-US';
  }

  function t(key) {
    var value = state.strings[key];
    if (value === undefined) {
      return key;
    }

    for (var i = 1; i < arguments.length; i++) {
      value = value.split('{' + (i - 1) + '}').join(arguments[i]);
    }

    return value;
  }

  /**
   * Translates a value drawn from a provider's controlled vocabulary — a status or a genre.
   *
   * Neither TVMaze nor AnimeSchedule serves localized text, so free-form fields such as summaries
   * stay in English. Statuses and genres are different: they come from a fixed list, which means they
   * can be mapped to real translations here. Anything not in the map falls back to the provider's own
   * wording rather than showing a missing-key placeholder.
   */
  function term(prefix, value) {
    if (!value) {
      return value;
    }

    // "Science-Fiction" and "To Be Determined" both have to reach ScienceFiction / ToBeDetermined.
    var key = prefix + value.replace(/[^A-Za-z0-9]/g, '')
      .replace(/^./, function (first) { return first.toUpperCase(); });

    var translated = state.strings[key];
    return translated === undefined ? value : translated;
  }

  function loadStrings(culture) {
    var url = ApiClient.getUrl('web/ConfigurationPage', { name: 'releasehub-' + culture + '.json' });
    return fetch(url).then(function (response) {
      return response.ok ? response.json() : {};
    }).catch(function () {
      // A missing translation file must not blank the UI; t() falls back to the key.
      return {};
    });
  }

  /* ----------------------------------------------------------------- api */

  function apiGet(path, params) {
    return ApiClient.getJSON(ApiClient.getUrl(API + '/' + path, params || {}));
  }

  function apiPost(path, body) {
    return ApiClient.ajax({
      type: 'POST',
      url: ApiClient.getUrl(API + '/' + path),
      data: JSON.stringify(body),
      contentType: 'application/json'
    });
  }

  /* ------------------------------------------------------------ dom utils */

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) {
      node.className = className;
    }
    if (text !== undefined && text !== null) {
      // textContent throughout: provider summaries and titles are untrusted text and must never be
      // parsed as markup.
      node.textContent = text;
    }
    return node;
  }

  function icon(name) {
    var node = el('span', 'material-icons releasehub-icon', name);
    node.setAttribute('aria-hidden', 'true');
    return node;
  }

  function clear(node) {
    while (node.firstChild) {
      node.removeChild(node.firstChild);
    }
  }

  /* -------------------------------------------------------- date handling */

  function localDate(item) {
    return item.ReleaseUtc ? new Date(item.ReleaseUtc) : null;
  }

  function dayKey(date) {
    // Bucket by the *viewer's* local day, not by UTC: a 02:00 JST broadcast belongs to a different
    // calendar day depending on where you are reading the page.
    return date.getFullYear() + '-' + (date.getMonth() + 1) + '-' + date.getDate();
  }

  function formatDayHeading(date, culture) {
    return date.toLocaleDateString(culture, {
      weekday: 'long',
      day: 'numeric',
      month: 'long'
    });
  }

  function formatTime(date, culture) {
    return date.toLocaleTimeString(culture, { hour: '2-digit', minute: '2-digit' });
  }

  function relativeDayLabel(date, culture) {
    var today = new Date();
    var startOfToday = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    var startOfDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    var diffDays = Math.round((startOfDate - startOfToday) / 86400000);

    if (diffDays === 0) {
      return t('Today');
    }
    if (diffDays === 1) {
      return t('Tomorrow');
    }
    if (diffDays > 1 && diffDays < 7) {
      return t('ThisWeek');
    }
    return t('Later');
  }

  function formatLastUpdated(iso, culture) {
    if (!iso) {
      return t('LastUpdated', t('Never'));
    }
    var date = new Date(iso);
    return t('LastUpdated', date.toLocaleString(culture));
  }

  /* ------------------------------------------------------------- chrome */

  function buildHeader() {
    var header = el('div', 'releasehub-header');

    var titleRow = el('div', 'releasehub-titleRow');
    titleRow.appendChild(el('h1', 'sectionTitle releasehub-title', t('AppTitle')));

    // No search affordance here on purpose: the Discover tab is the search entry point, and a second
    // magnifier next to it only made the header look like it had two of everything.
    header.appendChild(titleRow);
    header.appendChild(buildTabs());
    header.appendChild(buildFilters());

    return header;
  }

  function buildTabs() {
    var tabs = el('div', 'releasehub-tabs emby-tabs-slider');
    var views = [
      { id: 'calendar', label: t('Calendar'), glyph: 'calendar_month' },
      { id: 'upcoming', label: t('Upcoming'), glyph: 'upcoming' },
      { id: 'following', label: t('Following'), glyph: 'bookmark' },
      { id: 'discover', label: t('Discover'), glyph: 'travel_explore' }
    ];

    views.forEach(function (view) {
      var button = el('button', 'emby-tab-button releasehub-tab');
      button.type = 'button';
      if (state.view === view.id) {
        button.classList.add('emby-tab-button-active');
      }
      button.appendChild(icon(view.glyph));
      button.appendChild(el('span', 'releasehub-tabLabel', view.label));
      button.addEventListener('click', function () {
        setView(view.id);
      });
      tabs.appendChild(button);
    });

    return tabs;
  }

  function buildFilters() {
    var row = el('div', 'releasehub-filters');
    var filters = [
      { id: 'all', label: t('All') },
      { id: 'anime', label: t('Anime') },
      { id: 'tv', label: t('TvSeries') }
    ];

    filters.forEach(function (filter) {
      var button = el('button', 'emby-button releasehub-chip');
      button.type = 'button';
      if (state.filter === filter.id) {
        button.classList.add('releasehub-chip-active');
      }
      button.appendChild(el('span', null, filter.label));
      button.addEventListener('click', function () {
        state.filter = filter.id;
        render();
      });
      row.appendChild(button);
    });

    // Calendar is the only view a week grid makes sense in; the other two are plain sequences, so
    // they get the list/poster pair only.
    var layouts = state.view === 'calendar'
      ? [
        { id: 'list', glyph: 'view_list' },
        { id: 'week', glyph: 'calendar_view_week' },
        { id: 'poster', glyph: 'grid_view' }
      ]
      : [
        { id: 'list', glyph: 'view_list' },
        { id: 'poster', glyph: 'grid_view' }
      ];

    if (state.view === 'calendar' || state.view === 'upcoming' || state.view === 'following') {
      row.appendChild(el('div', 'releasehub-filterSpacer'));

      layouts.forEach(function (layout) {
        var button = el('button', 'emby-button releasehub-chip releasehub-layoutChip');
        button.type = 'button';
        button.title = t('Layout' + layout.id.charAt(0).toUpperCase() + layout.id.slice(1));
        button.setAttribute('aria-label', button.title);
        if (state.layout === layout.id) {
          button.classList.add('releasehub-chip-active');
        }
        button.appendChild(icon(layout.glyph));
        button.appendChild(el('span', 'releasehub-layoutLabel', button.title));
        button.addEventListener('click', function () {
          state.layout = layout.id;
          state.weekOffset = 0;
          saveLayoutPreference(layout.id);
          render();
        });
        row.appendChild(button);
      });

      // The week view always shows exactly one week, so a day-range selector would contradict it;
      // it navigates week by week instead. Upcoming has its own fixed horizon.
      if (state.view === 'calendar' && state.layout !== 'week') {
        row.appendChild(el('div', 'releasehub-filterDivider'));

        [7, 14, 30].forEach(function (days) {
          var button = el('button', 'emby-button releasehub-chip');
          button.type = 'button';
          if (state.range === days) {
            button.classList.add('releasehub-chip-active');
          }
          button.appendChild(el('span', null, t('Range' + days)));
          button.addEventListener('click', function () {
            state.range = days;
            render();
          });
          row.appendChild(button);
        });
      }
    }

    return row;
  }

  /* -------------------------------------------------------------- cards */

  function posterFor(item) {
    var wrapper = el('div', 'releasehub-poster cardImageContainer');

    if (item.PosterUrl) {
      var img = el('img', 'releasehub-posterImage');
      // Library items come back as a relative Jellyfin image path so the browser reuses artwork the
      // server already has; provider items carry an absolute remote URL.
      img.src = /^https?:/i.test(item.PosterUrl)
        ? item.PosterUrl
        : ApiClient.getUrl(item.PosterUrl, { maxWidth: 240 });
      img.alt = '';
      img.loading = 'lazy';
      img.addEventListener('error', function () {
        img.remove();
        wrapper.appendChild(icon('image_not_supported'));
      });
      wrapper.appendChild(img);
    } else {
      wrapper.appendChild(icon('live_tv'));
    }

    return wrapper;
  }

  function badge(text, extraClass) {
    return el('span', 'releasehub-badge ' + (extraClass || ''), text);
  }

  function episodeLabel(item) {
    if (item.SeasonNumber && item.EpisodeNumber) {
      return t('SeasonEpisodeShort', item.SeasonNumber, item.EpisodeNumber);
    }
    if (item.EpisodeNumber) {
      return t('EpisodeShort', item.EpisodeNumber);
    }
    return null;
  }

  function subDubBadge(item) {
    if (item.SubDub === 'Sub') {
      return badge(t('Sub'), 'releasehub-badge-sub');
    }
    if (item.SubDub === 'Dub') {
      return badge(t('Dub'), 'releasehub-badge-dub');
    }
    if (item.SubDub === 'Raw') {
      return badge(t('Raw'), 'releasehub-badge-raw');
    }
    return null;
  }

  function releaseRow(item, culture, options) {
    var row = el('div', 'releasehub-release listItem');
    row.appendChild(posterFor(item));

    var body = el('div', 'releasehub-releaseBody listItemBody');

    var titleLine = el('div', 'releasehub-releaseTitle');
    titleLine.appendChild(el('span', null, item.Title));
    if (item.IsInLibrary) {
      titleLine.appendChild(badge(t('InLibrary'), 'releasehub-badge-library'));
    } else if (item.IsFollowed) {
      titleLine.appendChild(badge(t('Followed'), 'releasehub-badge-followed'));
    }
    body.appendChild(titleLine);

    var meta = el('div', 'releasehub-releaseMeta secondaryText');
    var episode = episodeLabel(item);
    if (episode) {
      meta.appendChild(el('span', null, episode));
    }
    if (item.EpisodeTitle) {
      meta.appendChild(el('span', 'releasehub-episodeTitle', item.EpisodeTitle));
    }
    body.appendChild(meta);

    var footer = el('div', 'releasehub-releaseFooter secondaryText');

    var date = localDate(item);
    if (options && options.showDate && date) {
      footer.appendChild(el('span', 'releasehub-when', formatDayHeading(date, culture)));
    }

    if (date && item.HasReleaseTime) {
      footer.appendChild(el('span', 'releasehub-time', formatTime(date, culture)));
    } else if (item.Certainty === 'Exact') {
      // A confirmed date with no broadcast time: say so instead of rendering a placeholder hour.
      footer.appendChild(el('span', 'releasehub-time releasehub-muted', '—'));
    }

    if (item.Certainty === 'Approximate') {
      footer.appendChild(badge(item.ApproximateLabel || t('ApproximateDate'), 'releasehub-badge-approx'));
    } else if (item.Certainty === 'AnnouncedNoDate') {
      footer.appendChild(badge(t('NoDateAnnounced'), 'releasehub-badge-approx'));
    } else if (item.Certainty === 'Unknown') {
      footer.appendChild(badge(t('UnknownDate'), 'releasehub-badge-approx'));
    }

    var variant = subDubBadge(item);
    if (variant) {
      footer.appendChild(variant);
    }

    var platform = item.StreamingPlatform || item.Network;
    if (platform) {
      footer.appendChild(el('span', 'releasehub-platform', platform));
    }

    body.appendChild(footer);
    row.appendChild(body);
    return row;
  }

  /* -------------------------------------------------------------- views */

  function emptyState(titleKey, hintKey, glyph) {
    var box = el('div', 'releasehub-empty');
    box.appendChild(icon(glyph || 'event_busy'));
    box.appendChild(el('div', 'releasehub-emptyTitle', t(titleKey)));
    if (hintKey) {
      box.appendChild(el('div', 'releasehub-emptyHint secondaryText', t(hintKey)));
    }
    return box;
  }

  /**
   * Groups releases into consecutive local days, preserving chronological order.
   */
  function groupByDay(items) {
    var groups = {};
    var order = [];

    items.forEach(function (item) {
      var date = localDate(item);
      if (!date) {
        return;
      }

      var key = dayKey(date);
      if (!groups[key]) {
        groups[key] = { key: key, date: date, items: [] };
        order.push(key);
      }
      groups[key].items.push(item);
    });

    return order.map(function (key) {
      return groups[key];
    });
  }

  function renderCalendar(body, culture) {
    if (state.layout === 'week') {
      renderWeekCalendar(body, culture);
      return;
    }

    body.appendChild(loadingNode());

    apiGet('Calendar', { days: state.range, filter: state.filter }).then(function (response) {
      clear(body);
      body.appendChild(freshnessNote(response.LastSyncUtc, culture));

      if (!response.Items.length) {
        body.appendChild(emptyState('NoReleases', 'NoReleasesHint'));
        return;
      }

      var days = groupByDay(response.Items);

      if (effectiveLayout() === 'poster') {
        buildPosterLayout(days, culture).forEach(function (section) {
          body.appendChild(section);
        });
      } else {
        buildListLayout(days, culture).forEach(function (section) {
          body.appendChild(section);
        });
      }
    }, function () {
      clear(body);
      body.appendChild(errorState(function () { render(); }));
    });
  }

  /* --------------------------------------------------- layout: day list */

  function dayHeading(group, culture) {
    var heading = el('h2', 'releasehub-dayHeading sectionTitle');
    heading.appendChild(el('span', null, formatDayHeading(group.date, culture)));
    heading.appendChild(el('span', 'releasehub-dayRelative secondaryText',
      relativeDayLabel(group.date, culture)));
    return heading;
  }

  function buildListLayout(days, culture) {
    return days.map(function (group) {
      var section = el('div', 'releasehub-daySection verticalSection');
      section.appendChild(dayHeading(group, culture));

      var list = el('div', 'releasehub-dayItems paperList');
      group.items.forEach(function (item) {
        list.appendChild(releaseRow(item, culture, { showDate: false }));
      });
      section.appendChild(list);
      return section;
    });
  }

  /* ------------------------------------------------ layout: week columns */

  /**
   * Start of the week containing `date`, honouring the locale's first weekday where the browser
   * exposes it (Monday in France, Sunday in the US) and falling back to Monday.
   */
  function startOfWeek(date, culture) {
    var firstDay = 1;

    try {
      var locale = new Intl.Locale(culture);
      var info = locale.weekInfo || (locale.getWeekInfo && locale.getWeekInfo());
      if (info && typeof info.firstDay === 'number') {
        firstDay = info.firstDay % 7;
      }
    } catch (error) {
      /* older browsers: Monday is the safer default for the locales we ship */
    }

    var start = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    var delta = (start.getDay() - firstDay + 7) % 7;
    start.setDate(start.getDate() - delta);
    return start;
  }

  function daysBetween(from, to) {
    return Math.round((to - from) / 86400000);
  }

  function renderWeekCalendar(body, culture) {
    var today = new Date();
    var weekStart = startOfWeek(today, culture);
    weekStart.setDate(weekStart.getDate() + (state.weekOffset * 7));

    var offset = daysBetween(new Date(today.getFullYear(), today.getMonth(), today.getDate()), weekStart);

    body.appendChild(loadingNode());

    apiGet('Calendar', { days: 7, offset: offset, filter: state.filter }).then(function (response) {
      clear(body);
      body.appendChild(freshnessNote(response.LastSyncUtc, culture));
      body.appendChild(buildWeekNav(weekStart, culture));
      body.appendChild(buildWeekLayout(weekStart, response.Items, culture));
    }, function () {
      clear(body);
      body.appendChild(errorState(function () { render(); }));
    });
  }

  function buildWeekNav(weekStart, culture) {
    var nav = el('div', 'releasehub-weekNav');

    var previous = el('button', 'paper-icon-button-light releasehub-weekNavButton');
    previous.type = 'button';
    previous.title = t('PreviousWeek');
    previous.setAttribute('aria-label', previous.title);
    previous.appendChild(icon('chevron_left'));
    // The cache only holds a small look-back, so paging further into the past would show empty weeks
    // that are an artefact of the cache rather than the truth.
    previous.disabled = state.weekOffset <= 0;
    previous.addEventListener('click', function () {
      state.weekOffset -= 1;
      render();
    });

    var next = el('button', 'paper-icon-button-light releasehub-weekNavButton');
    next.type = 'button';
    next.title = t('NextWeek');
    next.setAttribute('aria-label', next.title);
    next.appendChild(icon('chevron_right'));
    next.disabled = state.weekOffset >= 12;
    next.addEventListener('click', function () {
      state.weekOffset += 1;
      render();
    });

    var weekEnd = new Date(weekStart);
    weekEnd.setDate(weekEnd.getDate() + 6);

    var label = el('div', 'releasehub-weekRange');
    label.textContent = weekStart.toLocaleDateString(culture, { day: 'numeric', month: 'short' })
      + ' – '
      + weekEnd.toLocaleDateString(culture, { day: 'numeric', month: 'short', year: 'numeric' });

    nav.appendChild(previous);
    nav.appendChild(label);
    nav.appendChild(next);

    if (state.weekOffset !== 0) {
      var todayButton = el('button', 'emby-button releasehub-chip releasehub-weekTodayButton');
      todayButton.type = 'button';
      todayButton.appendChild(el('span', null, t('Today')));
      todayButton.addEventListener('click', function () {
        state.weekOffset = 0;
        render();
      });
      nav.appendChild(todayButton);
    }

    return nav;
  }

  function buildWeekLayout(weekStart, items, culture) {
    // Every day of the week gets a column, including the empty ones: a calendar that silently omits
    // Monday reads as though Monday does not exist, rather than as though nothing airs on it.
    var byDay = {};
    items.forEach(function (item) {
      var date = localDate(item);
      if (!date) {
        return;
      }
      var key = dayKey(date);
      (byDay[key] = byDay[key] || []).push(item);
    });

    var grid = el('div', 'releasehub-weekGrid');
    var todayKey = dayKey(new Date());

    for (var offset = 0; offset < 7; offset++) {
      var date = new Date(weekStart);
      date.setDate(date.getDate() + offset);

      var key = dayKey(date);
      var column = el('div', 'releasehub-weekColumn');

      var header = el('div', 'releasehub-weekHeader');
      header.appendChild(el('div', 'releasehub-weekDay',
        date.toLocaleDateString(culture, { weekday: 'long', day: 'numeric' })));

      // The marker slot is always present, empty or not, so every column header is the same height.
      var marker = el('div', 'releasehub-weekToday');
      if (key === todayKey) {
        column.classList.add('releasehub-weekColumn-today');
        marker.textContent = t('Today');
      }
      header.appendChild(marker);
      column.appendChild(header);

      var list = el('div', 'releasehub-weekItems');
      var dayItems = byDay[key];

      if (dayItems && dayItems.length) {
        dayItems.forEach(function (item) {
          list.appendChild(weekCell(item, culture));
        });
      } else {
        list.appendChild(el('div', 'releasehub-weekEmpty', '—'));
      }

      column.appendChild(list);
      grid.appendChild(column);
    }

    return grid;
  }

  function weekCell(item, culture) {
    var cell = el('div', 'releasehub-weekCell');

    var title = el('div', 'releasehub-weekTitle', item.Title);
    cell.appendChild(title);

    var meta = el('div', 'releasehub-weekMeta secondaryText');
    var episode = episodeLabel(item);
    if (episode) {
      meta.appendChild(el('span', null, episode));
    }

    var date = localDate(item);
    if (date && item.HasReleaseTime) {
      meta.appendChild(el('span', 'releasehub-time', formatTime(date, culture)));
    }

    var variant = subDubBadge(item);
    if (variant) {
      meta.appendChild(variant);
    }

    if (item.IsInLibrary) {
      cell.classList.add('releasehub-weekCell-library');
    }

    cell.appendChild(meta);
    return cell;
  }

  /* ------------------------------------------------ layout: poster cards */

  function buildPosterLayout(days, culture) {
    return days.map(function (group) {
      var section = el('div', 'releasehub-daySection verticalSection');

      var heading = el('h2', 'releasehub-dayHeading sectionTitle');
      heading.appendChild(el('span', null,
        t('ReleasesOn', formatDayHeading(group.date, culture))));
      heading.appendChild(el('span', 'releasehub-dayRelative secondaryText',
        relativeDayLabel(group.date, culture)));
      section.appendChild(heading);

      var grid = el('div', 'releasehub-posterGrid');
      group.items.forEach(function (item) {
        grid.appendChild(posterCard(item, culture));
      });
      section.appendChild(grid);
      return section;
    });
  }

  /**
   * The layout actually usable in the current view.
   *
   * The week grid only makes sense on the calendar, but the stored preference is shared across views
   * so that switching to posters stays sticky everywhere. This resolves the mismatch instead of
   * letting Upcoming try to render a week it has no week for.
   */
  function effectiveLayout() {
    if (state.layout === 'week' && state.view !== 'calendar') {
      return 'list';
    }
    return state.layout;
  }

  function followedPosterCard(item) {
    var card = el('div', 'releasehub-posterCard card');

    var art = el('div', 'releasehub-posterArt cardImageContainer');
    if (item.PosterUrl) {
      var img = el('img', 'releasehub-posterImage');
      img.src = /^https?:/i.test(item.PosterUrl)
        ? item.PosterUrl
        : ApiClient.getUrl(item.PosterUrl, { maxWidth: 400 });
      img.alt = '';
      img.loading = 'lazy';
      img.addEventListener('error', function () {
        img.remove();
        art.appendChild(icon('live_tv'));
      });
      art.appendChild(img);
    } else {
      art.appendChild(icon('live_tv'));
    }
    card.appendChild(art);

    card.appendChild(el('div', 'releasehub-posterTitle cardText', item.Title));

    var footer = el('div', 'releasehub-posterFooter cardText-secondary');
    footer.appendChild(el('div', null, item.IsAnime ? t('Anime') : t('TvSeries')));

    var button = el('button', 'emby-button raised releasehub-posterAction');
    button.type = 'button';
    button.appendChild(el('span', null, t('Unfollow')));
    button.addEventListener('click', function () {
      button.disabled = true;
      apiPost('Unfollow', { Provider: item.Provider, ProviderId: item.ProviderId })
        .then(function () { render(); }, function () { button.disabled = false; });
    });
    footer.appendChild(button);

    card.appendChild(footer);
    return card;
  }

  function posterCard(item, culture, options) {
    var card = el('div', 'releasehub-posterCard card');

    var art = el('div', 'releasehub-posterArt cardImageContainer');
    if (item.PosterUrl) {
      var img = el('img', 'releasehub-posterImage');
      img.src = /^https?:/i.test(item.PosterUrl)
        ? item.PosterUrl
        : ApiClient.getUrl(item.PosterUrl, { maxWidth: 400 });
      img.alt = '';
      img.loading = 'lazy';
      img.addEventListener('error', function () {
        img.remove();
        art.appendChild(icon('live_tv'));
      });
      art.appendChild(img);
    } else {
      art.appendChild(icon('live_tv'));
    }

    var tags = el('div', 'releasehub-posterTags');
    if (item.IsInLibrary) {
      tags.appendChild(badge(t('InLibrary'), 'releasehub-badge-library'));
    } else if (item.IsFollowed) {
      tags.appendChild(badge(t('Followed'), 'releasehub-badge-followed'));
    }
    var variant = subDubBadge(item);
    if (variant) {
      tags.appendChild(variant);
    }
    if (tags.childNodes.length) {
      art.appendChild(tags);
    }

    card.appendChild(art);
    card.appendChild(el('div', 'releasehub-posterTitle cardText', item.Title));

    var footer = el('div', 'releasehub-posterFooter cardText-secondary');

    var date = localDate(item);

    if (options && options.showDate && date) {
      var day = el('div', 'releasehub-posterDate');
      day.appendChild(icon('event'));
      day.appendChild(el('span', null,
        date.toLocaleDateString(culture, { day: 'numeric', month: 'short' })));
      footer.appendChild(day);
    }

    if (date && item.HasReleaseTime) {
      var time = el('div', 'releasehub-posterTime');
      time.appendChild(icon('schedule'));
      time.appendChild(el('span', null, formatTime(date, culture)));
      footer.appendChild(time);
    } else if (item.Certainty !== 'Exact') {
      footer.appendChild(el('div', 'releasehub-posterTime releasehub-muted',
        item.ApproximateLabel || t('ApproximateDate')));
    }

    var episode = episodeLabel(item);
    if (episode) {
      var line = el('div', 'releasehub-posterEpisode');
      line.appendChild(icon('live_tv'));
      line.appendChild(el('span', null, episode));
      footer.appendChild(line);
    }

    card.appendChild(footer);
    return card;
  }

  function renderUpcoming(body, culture) {
    body.appendChild(loadingNode());

    apiGet('Upcoming', { filter: state.filter }).then(function (response) {
      clear(body);
      body.appendChild(freshnessNote(response.LastSyncUtc, culture));

      if (!response.Items.length) {
        body.appendChild(emptyState('NoReleases', 'NoReleasesHint'));
        return;
      }

      // Bucketed here rather than server-side: Today/Tomorrow depend on the viewer's timezone, which
      // only the browser knows.
      var buckets = [
        { key: 'Today', items: [] },
        { key: 'Tomorrow', items: [] },
        { key: 'ThisWeek', items: [] },
        { key: 'Later', items: [] }
      ];

      response.Items.forEach(function (item) {
        var date = localDate(item);
        if (!date) {
          return;
        }
        var label = relativeDayLabel(date, culture);
        for (var i = 0; i < buckets.length; i++) {
          if (t(buckets[i].key) === label) {
            buckets[i].items.push(item);
            return;
          }
        }
      });

      var posters = effectiveLayout() === 'poster';

      buckets.forEach(function (bucket) {
        if (!bucket.items.length) {
          return;
        }

        var section = el('div', 'releasehub-daySection verticalSection');
        section.appendChild(el('h2', 'releasehub-dayHeading sectionTitle', t(bucket.key)));

        if (posters) {
          var grid = el('div', 'releasehub-posterGrid');
          bucket.items.forEach(function (item) {
            grid.appendChild(posterCard(item, culture, { showDate: bucket.key === 'Later' }));
          });
          section.appendChild(grid);
        } else {
          var list = el('div', 'releasehub-dayItems paperList');
          bucket.items.forEach(function (item) {
            list.appendChild(releaseRow(item, culture, { showDate: bucket.key === 'Later' }));
          });
          section.appendChild(list);
        }

        body.appendChild(section);
      });
    }, function () {
      clear(body);
      body.appendChild(errorState(function () { render(); }));
    });
  }

  function renderFollowing(body, culture) {
    body.appendChild(loadingNode());

    apiGet('Following').then(function (items) {
      clear(body);

      var filtered = items.filter(function (item) {
        if (state.filter === 'anime') {
          return item.IsAnime;
        }
        if (state.filter === 'tv') {
          return !item.IsAnime;
        }
        return true;
      });

      if (!filtered.length) {
        body.appendChild(emptyState('NoFollowedItems', 'NoFollowedItemsHint', 'bookmark_border'));
        return;
      }

      if (effectiveLayout() === 'poster') {
        var posters = el('div', 'releasehub-posterGrid');
        filtered.forEach(function (item) {
          posters.appendChild(followedPosterCard(item));
        });
        body.appendChild(posters);
        return;
      }

      var grid = el('div', 'releasehub-grid');
      filtered.forEach(function (item) {
        grid.appendChild(followedCard(item, culture));
      });
      body.appendChild(grid);
    }, function () {
      clear(body);
      body.appendChild(errorState(function () { render(); }));
    });
  }

  function followedCard(item, culture) {
    var card = el('div', 'releasehub-card card');
    card.appendChild(posterFor(item));

    var info = el('div', 'releasehub-cardInfo');
    info.appendChild(el('div', 'releasehub-cardTitle cardText', item.Title));
    info.appendChild(el('div', 'releasehub-cardSub cardText-secondary',
      item.IsAnime ? t('Anime') : t('TvSeries')));

    var button = el('button', 'emby-button raised releasehub-cardAction');
    button.type = 'button';
    button.appendChild(el('span', null, t('Unfollow')));
    button.addEventListener('click', function () {
      button.disabled = true;
      apiPost('Unfollow', { Provider: item.Provider, ProviderId: item.ProviderId })
        .then(function () { render(); }, function () { button.disabled = false; });
    });

    info.appendChild(button);
    card.appendChild(info);
    return card;
  }

  /** Shortest query worth sending: one letter matches most of a provider's catalogue. */
  var MIN_QUERY_LENGTH = 2;

  /** Idle time before an as-you-type query is sent. */
  var SEARCH_DEBOUNCE_MS = 450;

  function renderDiscover(body, culture) {
    var field = el('div', 'releasehub-searchField');

    var glyph = icon('search');
    glyph.classList.add('releasehub-searchIcon');
    field.appendChild(glyph);

    var input = el('input', 'emby-input releasehub-searchInput');
    input.type = 'search';
    input.placeholder = t('SearchPlaceholder');
    input.value = state.query;
    input.setAttribute('aria-label', t('Search'));
    input.autocomplete = 'off';
    field.appendChild(input);

    var spinner = el('span', 'releasehub-searchSpinner secondaryText');
    field.appendChild(spinner);

    var results = el('div', 'releasehub-results');

    var timer = null;
    var sequence = 0;

    function runSearch() {
      var query = input.value.trim();
      state.query = query;

      if (query.length < MIN_QUERY_LENGTH) {
        spinner.textContent = '';
        clear(results);
        results.appendChild(emptyState('Search', 'SearchHint', 'search'));
        return;
      }

      // Every keystroke that survives the debounce still races the previous one over the network;
      // the sequence number makes sure a slow earlier response cannot overwrite a newer one.
      var token = ++sequence;
      spinner.textContent = t('Loading');

      apiGet('Discover/Search', { q: query, filter: state.filter }).then(function (items) {
        if (token !== sequence) {
          return;
        }

        spinner.textContent = '';
        clear(results);

        if (!items.length) {
          results.appendChild(emptyState('NoResults', null, 'search_off'));
          return;
        }

        var grid = el('div', 'releasehub-grid');
        items.forEach(function (item) {
          grid.appendChild(discoverCard(item, culture, runSearch));
        });
        results.appendChild(grid);
      }, function () {
        if (token !== sequence) {
          return;
        }
        spinner.textContent = '';
        clear(results);
        results.appendChild(errorState(runSearch));
      });
    }

    function scheduleSearch() {
      // Debounced so that typing a word costs one provider request rather than one per letter. The
      // backend rate limiter is the hard guarantee; this simply avoids spending the budget for nothing.
      window.clearTimeout(timer);
      timer = window.setTimeout(runSearch, SEARCH_DEBOUNCE_MS);
    }

    input.addEventListener('input', scheduleSearch);
    input.addEventListener('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault();
        window.clearTimeout(timer);
        runSearch();
      }
    });

    body.appendChild(field);
    body.appendChild(results);

    if (state.query.length >= MIN_QUERY_LENGTH) {
      runSearch();
    } else {
      results.appendChild(emptyState('Search', 'SearchHint', 'search'));
    }

    // Deferred so the field is focusable before the browser paints the new view.
    setTimeout(function () { input.focus(); }, 0);
  }

  function discoverCard(item, culture, onChanged) {
    var card = el('div', 'releasehub-card releasehub-card-wide card');
    card.appendChild(posterFor(item));

    var info = el('div', 'releasehub-cardInfo');

    var titleLine = el('div', 'releasehub-cardTitle cardText');
    titleLine.appendChild(el('span', null, item.Title));
    if (item.Year) {
      titleLine.appendChild(el('span', 'releasehub-cardYear secondaryText', String(item.Year)));
    }
    info.appendChild(titleLine);

    if (item.OriginalTitle && item.OriginalTitle !== item.Title) {
      info.appendChild(el('div', 'releasehub-cardOriginal cardText-secondary', item.OriginalTitle));
    }

    var tags = el('div', 'releasehub-cardTags');
    if (item.IsInLibrary) {
      tags.appendChild(badge(t('InLibrary'), 'releasehub-badge-library'));
    }
    if (item.IsFollowed) {
      tags.appendChild(badge(t('Followed'), 'releasehub-badge-followed'));
    }
    if (item.Status) {
      tags.appendChild(badge(term('Status', item.Status), 'releasehub-badge-status'));
    }
    tags.appendChild(badge(item.Provider === 'AnimeSchedule' ? t('ProviderAnimeSchedule') : t('ProviderTvMaze'),
      'releasehub-badge-provider'));
    info.appendChild(tags);

    if (item.Genres && item.Genres.length) {
      var genres = item.Genres.map(function (genre) {
        return term('Genre', genre);
      }).join(' · ');
      info.appendChild(el('div', 'releasehub-cardGenres cardText-secondary', genres));
    }

    if (item.Summary) {
      info.appendChild(el('p', 'releasehub-cardSummary secondaryText', item.Summary));
    }

    var action = el('button', 'emby-button raised releasehub-cardAction');
    action.type = 'button';
    action.appendChild(el('span', null, item.IsFollowed ? t('Unfollow') : t('Follow')));
    if (!item.IsFollowed) {
      action.classList.add('button-submit');
    }
    action.addEventListener('click', function () {
      action.disabled = true;
      var endpoint = item.IsFollowed ? 'Unfollow' : 'Follow';
      apiPost(endpoint, { Provider: item.Provider, ProviderId: item.ProviderId })
        .then(onChanged, function () { action.disabled = false; });
    });
    info.appendChild(action);

    card.appendChild(info);
    return card;
  }

  /* ----------------------------------------------------------- feedback */

  function loadingNode() {
    var box = el('div', 'releasehub-loading secondaryText', t('Loading'));
    return box;
  }

  function errorState(retry) {
    var box = el('div', 'releasehub-empty');
    box.appendChild(icon('error_outline'));
    box.appendChild(el('div', 'releasehub-emptyTitle', t('ErrorTitle')));

    var button = el('button', 'emby-button raised', null);
    button.type = 'button';
    button.appendChild(el('span', null, t('Retry')));
    button.addEventListener('click', retry);
    box.appendChild(button);

    return box;
  }

  function freshnessNote(lastSyncUtc, culture) {
    // Always visible, so cached data never silently passes for live data when a provider is down.
    var note = el('div', 'releasehub-freshness secondaryText');
    note.appendChild(icon('schedule'));
    note.appendChild(el('span', null, formatLastUpdated(lastSyncUtc, culture)));
    return note;
  }

  /* -------------------------------------------------------------- shell */

  function setView(view) {
    state.view = view;
    render();
  }

  function render() {
    var container = state.container;
    if (!container) {
      return;
    }

    var culture = state.culture;
    clear(container);
    container.appendChild(buildHeader());

    var body = el('div', 'releasehub-body');
    container.appendChild(body);

    if (state.view === 'calendar') {
      renderCalendar(body, culture);
    } else if (state.view === 'upcoming') {
      renderUpcoming(body, culture);
    } else if (state.view === 'following') {
      renderFollowing(body, culture);
    } else {
      renderDiscover(body, culture);
    }

    container.appendChild(attribution());
  }

  function attribution() {
    // TVMaze is CC BY-SA and AnimeSchedule's terms require a visible credit, so this stays on screen
    // rather than being tucked away in documentation.
    var box = el('div', 'releasehub-attribution secondaryText');
    box.appendChild(el('span', null, t('SourceLabel') + ': '));

    var tvmaze = el('a', 'button-link', 'TVMaze');
    tvmaze.href = 'https://www.tvmaze.com/';
    tvmaze.target = '_blank';
    tvmaze.rel = 'noopener noreferrer';
    box.appendChild(tvmaze);

    box.appendChild(el('span', null, ' · '));

    var animeschedule = el('a', 'button-link', 'AnimeSchedule.net');
    animeschedule.href = 'https://animeschedule.net/';
    animeschedule.target = '_blank';
    animeschedule.rel = 'noopener noreferrer';
    box.appendChild(animeschedule);

    return box;
  }

  window.ReleaseHub = window.ReleaseHub || {};

  window.ReleaseHub.mount = function (container) {
    if (!container) {
      return;
    }

    state.container = container;
    loadLayoutPreference();
    clear(container);
    container.appendChild(loadingNode());

    apiGet('Status').then(function (status) {
      state.status = status;
      state.range = status.DefaultCalendarRangeDays || 7;
      state.culture = resolveCulture(status.Language);

      return loadStrings(state.culture).then(function (strings) {
        state.strings = strings;
        render();
      });
    }, function () {
      clear(container);
      state.culture = resolveCulture(null);
      container.appendChild(errorState(function () {
        window.ReleaseHub.mount(container);
      }));
    });
  };
})();
