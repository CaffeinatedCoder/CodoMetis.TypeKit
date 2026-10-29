// A link from a page to a file of the repository that is not a page of the site (a design note, a decision record,
// CONTRIBUTING.md, spikes/) becomes a GitHub address at the commit the site is built from (#115). The sources are
// never edited for the site.
//
// DocFX hands this script each page's HTML with its links still rooted at site/ (`~/../design/plan.md`), before it
// resolves and checks them. A link to a page is left as it is, so DocFX checks it and its anchor and fails the build
// with --warningsAsErrors when either is broken; a link rewritten here is checked by DocumentationLinkTests instead.
// The patterns are the content globs of site/docfx.json: change the two together.
var pages = [/^README\.md$/, /^CHANGELOG\.md$/, /^docs\/[^\/]+\.md$/, /^src\/[^\/]+\/README\.md$/];

exports.preTransform = function (model) {
  return model;
};

exports.postTransform = function (model) {
  var commit = model._commit;
  if (!commit) {
    throw new Error('site/template: no commit to link the repository at. Pass --metadata _commit=<sha>, as CONTRIBUTING.md shows.');
  }

  model.conceptual = model.conceptual.replace(/href="~\/\.\.\/([^"#]*)(#[^"]*)?"/g, function (link, path, anchor) {
    if (pages.some(function (page) { return page.test(path); })) return link;

    var kind = path === '' || path.charAt(path.length - 1) === '/' ? 'tree' : 'blob';
    return 'href="' + model._repository + '/' + kind + '/' + commit + '/' + path + (anchor || '') + '"';
  });
  return model;
};
