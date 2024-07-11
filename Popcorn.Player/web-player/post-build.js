// post-build.js
const fs = require('fs');
const path = './build/index.html';

fs.readFile(path, 'utf8', function(err, data) {
  if (err) {
    return console.error(err);
  }
  const result = data.replace('<head>', '<head><base href="http://app.local/">');

  fs.writeFile(path, result, 'utf8', function(err) {
    if (err) return console.error(err);
  });
  console.log('Patched index.html with <base> http://app.local/');
});