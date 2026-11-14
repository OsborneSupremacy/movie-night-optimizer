// Sends browser navigation to the SPA without answering every miss with the app.
//
// Rewriting on the way in, rather than turning S3's errors into /index.html on the way out, keeps
// routes and files apart: a missing asset comes back a genuine 404 instead of a 200 carrying HTML.
//
// Attached to the default behavior only. Requests under /api/ match their own behavior first
// and never reach this.
function handler(event) {
    var request = event.request;
    var lastSegment = request.uri.substring(request.uri.lastIndexOf('/') + 1);

    // A dot in the last segment means a file: /favicon.ico, /assets/index-p65XSSj-.js. Those go
    // to S3 as they are.
    if (lastSegment.indexOf('.') !== -1) {
        return request;
    }

    // Everything else is a route: "/", "/night/{nightId}". None of the app's routes carry a dot.
    // Only the origin fetch is rewritten; the address the browser holds is untouched, which is
    // what React Router reads the route out of.
    request.uri = '/index.html';

    return request;
}
