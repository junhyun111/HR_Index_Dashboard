import { build } from 'esbuild';

await Promise.all([
  build({
    entryPoints:['./ClientApp/organization-flow.jsx'],
    bundle:true,
    minify:true,
    format:'iife',
    target:'es2022',
    outfile:'./wwwroot/js/organization.js'
  }),
  build({
    entryPoints:['./ClientApp/react-flow.css'],
    bundle:true,
    minify:true,
    outfile:'./wwwroot/css/react-flow.css'
  })
]);
