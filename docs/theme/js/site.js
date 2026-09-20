// Two jobs, and neither needs a library: find a page by substring of its title or text, and put every wide
// table in its own scroller so the page body never moves sideways.
(function () {
  'use strict';

  var field = document.getElementById('find');
  var panel = document.getElementById('found');
  var base = document.querySelector('link[rel="stylesheet"][href$="site.css"]');
  var root = base ? base.getAttribute('href').replace(/css\/site\.css$/, '') : '';
  var index = null;
  var loading = null;

  function load() {
    if (index) return Promise.resolve(index);
    if (loading) return loading;

    loading = fetch(root + 'search/search_index.json')
      .then(function (answer) { return answer.json(); })
      .then(function (body) { index = body.docs || []; return index; })
      .catch(function () { index = []; return index; });

    return loading;
  }

  // Exact title, then title starts with, then title contains, then body contains. Four ranks are enough at
  // this size, and they are the ranks a reader expects without being told.
  function ranked(docs, query) {
    var needle = query.toLowerCase();
    var hits = [];

    docs.forEach(function (doc) {
      var title = (doc.title || '').toLowerCase();
      var rank = title === needle ? 0
        : title.indexOf(needle) === 0 ? 1
        : title.indexOf(needle) > -1 ? 2
        : (doc.text || '').toLowerCase().indexOf(needle) > -1 ? 3
        : -1;

      if (rank > -1) hits.push({ doc: doc, rank: rank });
    });

    hits.sort(function (a, b) { return a.rank - b.rank; });

    return hits.slice(0, 8);
  }

  function show(hits, query) {
    if (!hits.length) {
      panel.innerHTML = '<div class="sheet inner"><p class="none">Nothing matches “' +
        query.replace(/[<&]/g, '') + '”.</p></div>';
      panel.hidden = false;
      return;
    }

    var rows = hits.map(function (hit) {
      var doc = hit.doc;
      var where = (doc.location || '').split('#')[0] || '';
      return '<a href="' + root + doc.location + '"><span class="t"></span><span class="w"></span></a>';
    }).join('');

    panel.innerHTML = '<div class="sheet inner">' + rows + '</div>';

    // Titles are written as text rather than markup, so a heading with an angle bracket cannot become one.
    var anchors = panel.querySelectorAll('a');
    hits.forEach(function (hit, at) {
      anchors[at].querySelector('.t').textContent = hit.doc.title || hit.doc.location;
      anchors[at].querySelector('.w').textContent = (hit.doc.location || '').split('#')[0] || 'index';
    });

    panel.hidden = false;
  }

  if (field && panel) {
    field.addEventListener('input', function () {
      var query = field.value.trim();

      if (query.length < 2) { panel.hidden = true; return; }

      load().then(function (docs) { show(ranked(docs, query), query); });
    });

    field.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') { field.value = ''; panel.hidden = true; field.blur(); }
    });

    document.addEventListener('keydown', function (event) {
      if (event.key !== '/' || event.target === field) return;
      if (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA') return;

      event.preventDefault();
      field.focus();
    });

    document.addEventListener('click', function (event) {
      if (!panel.contains(event.target) && event.target !== field) panel.hidden = true;
    });
  }

  Array.prototype.forEach.call(document.querySelectorAll('.prose table'), function (table) {
    if (table.parentNode.classList.contains('scroll')) return;

    var box = document.createElement('div');
    box.className = 'scroll';
    table.parentNode.insertBefore(box, table);
    box.appendChild(table);
  });
}());
