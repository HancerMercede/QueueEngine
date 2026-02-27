#!/usr/bin/env node
/**
 * ╔══════════════════════════════════════════════════════════════════╗
 * ║              🧪  TestCraft — Project Scanner v3                 ║
 * ║                                                                  ║
 * ║  Uso / Usage:                                                    ║
 * ║    node testcraft-init.js            escanea e inicializa       ║
 * ║    node testcraft-init.js --scan     solo escanea, no escribe   ║
 * ║    node testcraft-init.js --status   muestra estado actual      ║
 * ║    node testcraft-init.js --refresh  re-escanea, preserva datos ║
 * ║    node testcraft-init.js --reset    borra el contexto          ║
 * ╚══════════════════════════════════════════════════════════════════╝
 */
"use strict";
const fs   = require("fs");
const path = require("path");

// COLORS
const C = {
  reset:"\x1b[0m", bold:"\x1b[1m", dim:"\x1b[2m",
  cyan:"\x1b[36m", green:"\x1b[32m", yellow:"\x1b[33m",
  red:"\x1b[31m",  white:"\x1b[37m",
};
const log   = (m="")  => console.log(m);
const ok    = (m)     => console.log(`${C.green}  ✔${C.reset}  ${m}`);
const info  = (m)     => console.log(`${C.cyan}  ℹ${C.reset}  ${m}`);
const warn  = (m)     => console.log(`${C.yellow}  ⚠${C.reset}  ${m}`);
const label = (k,v)   => console.log(`  ${C.cyan}◆${C.reset}  ${C.bold}${k}${C.reset}  ${C.white}${v}${C.reset}`);
const hr    = ()      => log(`${C.dim}${"─".repeat(58)}${C.reset}`);
const head  = (t)     => { log(); log(`${C.bold}${C.cyan}  ${t}${C.reset}`); hr(); };

// PATHS
const ROOT     = process.cwd();
const TC_DIR   = path.join(ROOT, ".testcraft");
const CTX_FILE = path.join(TC_DIR, "context.json");

const SKIP_DIRS = new Set([
  "node_modules","dist","build",".git",".svn","coverage","__pycache__",
  ".pytest_cache",".mypy_cache","venv",".venv","env",".env","vendor",
  "target","bin","obj",".idea",".vscode",".gradle",".mvn","out","tmp",
  ".next",".nuxt",".turbo","storybook-static","public","static","assets",
  "migrations","seeds","fixtures","docs","documentation",".testcraft",
]);

// FILE HELPERS
const abs      = (rel) => path.join(ROOT, rel);
const exists   = (rel) => fs.existsSync(abs(rel));
const readRaw  = (rel) => { try { return fs.readFileSync(abs(rel),"utf8"); } catch { return ""; } };
const readJSON = (rel) => { try { return JSON.parse(readRaw(rel)); } catch { return null; } };
const readAbs  = (p)   => { try { return fs.readFileSync(p,"utf8"); } catch { return ""; } };

function walkFiles(dir, exts, maxDepth=5) {
  const results = [];
  function walk(cur, d) {
    if (d > maxDepth) return;
    let items;
    try { items = fs.readdirSync(cur, { withFileTypes:true }); } catch { return; }
    for (const item of items) {
      if (SKIP_DIRS.has(item.name) || item.name.startsWith(".")) continue;
      const full = path.join(cur, item.name);
      if (item.isDirectory()) walk(full, d+1);
      else if (exts.some(e => item.name.endsWith(e))) results.push(path.relative(ROOT, full));
    }
  }
  walk(dir, 0);
  return results;
}

function walkDirs(dir, maxDepth=4) {
  const results = [];
  function walk(cur, d) {
    if (d > maxDepth) return;
    let items;
    try { items = fs.readdirSync(cur, { withFileTypes:true }); } catch { return; }
    for (const item of items) {
      if (!item.isDirectory() || SKIP_DIRS.has(item.name) || item.name.startsWith(".")) continue;
      const full = path.join(cur, item.name);
      results.push(path.relative(ROOT, full));
      walk(full, d+1);
    }
  }
  walk(dir, 0);
  return results;
}

// ARCHITECTURE DETECTOR
function detectArchitecture(allDirs) {
  const s = new Set(allDirs.map(d => d.toLowerCase().replace(/\\/g,"/")));
  const has    = (...p) => p.every(x => [...s].some(d => d.includes(x)));
  const hasAny = (...p) => p.some(x => [...s].some(d => d.includes(x)));

  if (has("domain") && has("application") && hasAny("infrastructure","infra"))
    return { pattern:"Hexagonal / Clean Architecture", layers:["domain","application","infrastructure"],
      testStrategy:"Unit test domain + application heavily. Integration test infrastructure adapters.",
      suggestedTestDirs:["domain","application","infrastructure"] };

  if (hasAny("aggregates","entities","valueobjects") && has("domain"))
    return { pattern:"Domain-Driven Design (DDD)", layers:["domain","application","infrastructure"],
      testStrategy:"Unit test aggregates and domain logic. Integration test repositories.",
      suggestedTestDirs:["domain","application"] };

  if (hasAny("features","modules") && hasAny("controllers","resolvers","handlers"))
    return { pattern:"Feature / Module-based", layers:["features","modules"],
      testStrategy:"Test each feature/module independently. Co-locate tests with source.",
      suggestedTestDirs:["features","modules"] };

  if (hasAny("controllers","handlers","routes") && hasAny("services","usecases","use-cases") && hasAny("repositories","models"))
    return { pattern:"Layered / MVC", layers:["controllers","services","repositories","models"],
      testStrategy:"Unit test services with mocked repos. Integration test controllers via HTTP.",
      suggestedTestDirs:["services","controllers","repositories"] };

  if (hasAny("components","pages","views") && hasAny("hooks","composables","stores"))
    return { pattern:"Component-based (Frontend)", layers:["components","pages","hooks","stores"],
      testStrategy:"Unit test hooks/composables/stores. Component test with testing-library. E2E for user flows.",
      suggestedTestDirs:["components","hooks","stores"] };

  if (hasAny("commands","queries") && hasAny("handlers","command-handlers","query-handlers"))
    return { pattern:"CQRS", layers:["commands","queries","handlers","events"],
      testStrategy:"Unit test each command/query handler. Integration test the full pipeline.",
      suggestedTestDirs:["commands","queries","handlers"] };

  return { pattern:"Flat / Simple", layers:[],
    testStrategy:"Unit test each module or function. Integration test entry points.",
    suggestedTestDirs:["src","lib","app"] };
}

// FOLDER STRUCTURE MAPPER
function mapFolderStructure(allDirs, srcFiles) {
  const important = [
    "src","lib","app","core","domain","application","infrastructure","infra",
    "features","modules","controllers","services","repositories","models",
    "entities","aggregates","handlers","commands","queries","events",
    "routes","middleware","utils","helpers","components","pages","views",
    "hooks","composables","stores","api","pkg","cmd","internal",
  ];
  const structure = {};
  for (const dir of important) {
    const sep = path.sep;
    const files = srcFiles.filter(f => f.startsWith(dir+sep) || f.startsWith(dir+"/"));
    if (files.length === 0) continue;
    const subDirs = [...new Set(files.map(f => {
      const parts = f.replace(/\\/g,"/").split("/");
      return parts.length > 2 ? parts[1] : null;
    }).filter(Boolean))];
    structure[dir] = { fileCount:files.length, subDirs, files:files.slice(0,20) };
  }
  return structure;
}

// TEST STYLE LEARNER
function learnTestStyle(testFiles) {
  if (testFiles.length === 0) return null;
  const samples = testFiles.slice(0,8);
  const p = {
    describeStyle:[], itStyle:[], assertStyle:[], mockStyle:[],
    factoryPatterns:[], importPatterns:[], fileNaming:null,
    coLocation:false, separateDir:false, aaaComments:false, snippets:[],
  };

  p.fileNaming = testFiles.some(f=>f.includes(".spec.")) && !testFiles.some(f=>f.includes(".test.")) ? ".spec" : ".test";
  p.separateDir = testFiles.some(f => /^(tests?|spec|__tests__)[\\/]/.test(f));
  p.coLocation  = testFiles.some(f => !/^(tests?|spec|__tests__)[\\/]/.test(f));

  for (const tf of samples) {
    const content = readRaw(tf);
    if (!content) continue;
    const lines = content.split("\n");

    const snippet = lines.filter(l=>l.trim()&&!l.trim().startsWith("//")&&!l.trim().startsWith("*")).slice(0,25).join("\n");
    if (snippet) p.snippets.push({ file:tf, snippet });

    p.describeStyle.push(...lines.filter(l=>/describe\s*\(/.test(l)).map(l=>l.trim()).slice(0,3));
    p.itStyle.push(...lines.filter(l=>/^\s*(it|test)\s*\(/.test(l)).map(l=>l.trim()).slice(0,4));

    if (content.includes("expect("))              p.assertStyle.push("jest/vitest expect");
    if (content.includes(".should."))             p.assertStyle.push("chai should");
    if (content.includes("assert("))              p.assertStyle.push("assert");
    if (content.includes("FluentAssertions")||content.includes(".Should()")) p.assertStyle.push("FluentAssertions");
    if (content.includes("assertThat("))          p.assertStyle.push("AssertJ/Hamcrest");
    if (content.includes("assertEquals"))         p.assertStyle.push("JUnit Assert");
    if (content.includes("vi.mock(")||content.includes("vi.fn()"))   p.mockStyle.push("Vitest vi.mock/vi.fn");
    if (content.includes("jest.mock(")||content.includes("jest.fn()")) p.mockStyle.push("Jest jest.mock/jest.fn");
    if (content.includes("@Mock")||content.includes("Mockito.when")) p.mockStyle.push("Mockito");
    if (content.includes("sinon."))               p.mockStyle.push("Sinon.js");
    if (content.includes("every {")||content.includes("MockK"))      p.mockStyle.push("MockK");
    if (content.includes("mocker.patch")||content.includes("unittest.mock")) p.mockStyle.push("pytest-mock");
    if (content.includes("new Mock<"))            p.mockStyle.push("Moq (.NET)");
    if (content.includes("factory(")||content.includes("FactoryBot")||content.includes("factory_boy")) p.factoryPatterns.push("Factory pattern");
    if (content.includes("Builder")||content.includes(".build()"))   p.factoryPatterns.push("Builder pattern");
    if (content.includes("faker")||content.includes("Faker"))        p.factoryPatterns.push("Faker data");
    if (content.includes("makeUser")||content.includes("createUser")||content.includes("buildUser")) p.factoryPatterns.push("Custom helpers");
    if (content.includes("// Arrange")||content.includes("# Arrange")) p.aaaComments = true;

    p.importPatterns.push(...lines.filter(l=>/^import|^from|^require|^using/.test(l.trim())).slice(0,6));
  }

  p.describeStyle  = [...new Set(p.describeStyle)].slice(0,6);
  p.itStyle        = [...new Set(p.itStyle)].slice(0,8);
  p.assertStyle    = [...new Set(p.assertStyle)];
  p.mockStyle      = [...new Set(p.mockStyle)];
  p.factoryPatterns= [...new Set(p.factoryPatterns)];
  p.importPatterns = [...new Set(p.importPatterns)].slice(0,12);
  return p;
}

// COVERAGE ANALYZER
function analyzeCoverage(srcFiles, testFiles) {
  const tested=[], untested=[];
  const testBases = testFiles.map(f =>
    path.basename(f)
      .replace(/\.(test|spec)\.(ts|js|tsx|jsx|py|rb|go|kt|java|cs|rs)$/,"")
      .replace(/_(test|spec)\.(py|go|rs)$/,"")
      .replace(/(Test|Spec)\.(java|kt|cs|rb)$/,"")
      .toLowerCase()
  );
  for (const src of srcFiles) {
    const base = path.basename(src).replace(/\.(ts|js|tsx|jsx|py|rb|go|kt|java|cs|rs)$/,"").toLowerCase();
    const covered = testBases.some(tb => tb===base||tb===base+"service"||tb===base+"controller"||tb.startsWith(base)||base.startsWith(tb));
    covered ? tested.push(src) : untested.push(src);
  }
  return { tested, untested, coveragePercent:Math.round((tested.length/Math.max(srcFiles.length,1))*100) };
}

// ECOSYSTEM DETECTORS
function detectNode() {
  const pkg = readJSON("package.json");
  if (!pkg) return null;
  const all = { ...(pkg.dependencies||{}), ...(pkg.devDependencies||{}), ...(pkg.peerDependencies||{}) };
  const has  = (...n) => n.some(x => x in all);

  const framework =
    has("@nestjs/core") ? "NestJS" : has("next") ? "Next.js" : has("nuxt") ? "Nuxt" :
    has("@remix-run/react") ? "Remix" : has("astro") ? "Astro" :
    has("svelte") ? "SvelteKit/Svelte" : has("vue") ? "Vue" :
    has("react") ? "React" : has("@angular/core") ? "Angular" :
    has("fastify") ? "Fastify" : has("express") ? "Express" :
    has("koa") ? "Koa" : has("@hapi/hapi") ? "Hapi" : "Node.js";

  const lang = has("typescript","ts-node","@types/node") ? "TypeScript" : "JavaScript";
  const testFW  = has("vitest") ? "Vitest" : has("jest","@jest/core","ts-jest") ? "Jest" : has("mocha") ? "Mocha" : null;
  const mockLib = has("msw") ? "MSW" : has("@testing-library/react","@testing-library/vue") ? "Testing Library" : has("sinon") ? "Sinon.js" : null;
  const e2e     = has("@playwright/test","playwright") ? "Playwright" : has("cypress") ? "Cypress" : null;

  const cfgs = ["jest.config.js","jest.config.ts","jest.config.mjs","vitest.config.js","vitest.config.ts",
    "vitest.config.mjs","cypress.config.js","cypress.config.ts","playwright.config.js","playwright.config.ts"].filter(exists);

  const testExts = lang==="TypeScript" ? [".test.ts",".test.tsx",".spec.ts",".spec.tsx"] : [".test.js",".test.jsx",".spec.js",".spec.jsx"];
  const srcExts  = lang==="TypeScript" ? [".ts",".tsx"] : [".js",".jsx"];
  const allTestFiles = walkFiles(ROOT, testExts.concat([".test.js",".test.ts"]));
  const allSrcFiles  = walkFiles(ROOT, srcExts.concat([".js",".ts"])).filter(f =>
    !f.includes(".test.")&&!f.includes(".spec.")&&!f.includes("config.")&&!f.includes(".config.")&&!f.startsWith("testcraft")
  );

  return {
    runtime:"Node.js", language:lang, framework,
    packageManager: exists("bun.lockb")?"Bun":exists("pnpm-lock.yaml")?"pnpm":exists("yarn.lock")?"Yarn":"npm",
    existing:{ testFramework:testFW, mockLib, e2eTool:e2e, configFiles:cfgs, testScript:pkg.scripts?.test||"npm test" },
    meta:{ name:pkg.name||path.basename(ROOT), version:pkg.version||"0.0.0", hasTypeScript:lang==="TypeScript", hasCoverage:cfgs.length>0 },
    files:{ testExts, srcExts, testDirs:["__tests__","test","tests","spec"].filter(exists), allTestFiles, allSrcFiles },
  };
}

function detectPython() {
  if (!exists("pyproject.toml")&&!exists("setup.py")&&!exists("requirements.txt")&&!exists("Pipfile")) return null;
  const all = readRaw("pyproject.toml")+readRaw("requirements.txt")+readRaw("requirements-dev.txt")+readRaw("setup.cfg");
  const has = (...w) => w.some(x => all.toLowerCase().includes(x.toLowerCase()));
  const framework = has("fastapi")?"FastAPI":has("django")?"Django":has("flask")?"Flask":has("starlette")?"Starlette":"Python";
  const allTestFiles = walkFiles(ROOT,[".py"]).filter(f=>path.basename(f).startsWith("test_")||f.endsWith("_test.py"));
  const allSrcFiles  = walkFiles(ROOT,[".py"]).filter(f=>!path.basename(f).startsWith("test_")&&!f.endsWith("_test.py")&&!f.includes("conftest")&&!f.includes("migration"));
  return {
    runtime:"Python", language:"Python", framework,
    packageManager: exists("Pipfile")?"Pipenv":exists("pyproject.toml")?"Poetry/pip":"pip",
    existing:{ testFramework:has("pytest")?"Pytest":has("unittest")?"Unittest":null,
      mockLib:has("pytest-mock","unittest.mock")?"pytest-mock":null,
      e2eTool:has("playwright")?"Playwright":has("selenium")?"Selenium":null,
      configFiles:["pytest.ini","setup.cfg","pyproject.toml","tox.ini"].filter(exists), testScript:"pytest" },
    meta:{ name:path.basename(ROOT), version:"0.0.0", hasAsync:has("asyncio","httpx","pytest-asyncio"), hasCoverage:has("pytest-cov") },
    files:{ testExts:["_test.py"], srcExts:[".py"], testDirs:["tests","test","spec"].filter(exists), allTestFiles, allSrcFiles },
  };
}

function detectJava() {
  if (!exists("pom.xml")&&!exists("build.gradle")&&!exists("build.gradle.kts")) return null;
  const all = readRaw("pom.xml")+readRaw("build.gradle")+readRaw("build.gradle.kts");
  const has = (...w) => w.some(x => all.includes(x));
  const lang = has("kotlin","kotlin-stdlib","org.jetbrains.kotlin")?"Kotlin":"Java";
  const framework = has("spring-boot","org.springframework.boot")?"Spring Boot":has("io.quarkus")?"Quarkus":lang;
  const ext = lang==="Kotlin"?[".kt"]:[".java"];
  const allTestFiles = walkFiles(ROOT,ext).filter(f=>f.includes("/test/")||f.endsWith("Test."+ext[0].slice(1))||f.endsWith("Spec."+ext[0].slice(1)));
  const allSrcFiles  = walkFiles(ROOT,ext).filter(f=>f.includes("/main/"));
  return {
    runtime:lang==="Kotlin"?"JVM/Kotlin":"JVM/Java", language:lang, framework,
    packageManager: exists("pom.xml")?"Maven":"Gradle",
    existing:{ testFramework:has("junit-jupiter","junit5")?"JUnit 5":has("testng")?"TestNG":null,
      mockLib:has("mockito")?"Mockito":has("mockk")?"MockK":null,
      assertLib:has("assertj")?"AssertJ":has("hamcrest")?"Hamcrest":null,
      e2eTool:has("selenium")?"Selenium":null,
      configFiles:["pom.xml","build.gradle","build.gradle.kts"].filter(exists),
      testScript:exists("pom.xml")?"mvn test":"./gradlew test" },
    meta:{ name:path.basename(ROOT), version:"0.0.0", hasCoverage:has("jacoco") },
    files:{ testExts:ext, srcExts:ext, testDirs:["src/test/java","src/test/kotlin"].filter(exists), allTestFiles, allSrcFiles },
  };
}

function detectDotNet() {
  const csproj = walkFiles(ROOT,[".csproj",".fsproj"]);
  if (csproj.length===0&&!walkFiles(ROOT,[".sln"]).length) return null;
  const allContent = csproj.map(f=>readAbs(path.join(ROOT,f))).join("\n");
  const has = (...w) => w.some(x => allContent.includes(x));
  const framework = has("Microsoft.AspNetCore","WebApplication")?"ASP.NET Core":has("Blazor")?"Blazor":".NET";
  const allTestFiles = walkFiles(ROOT,[".cs"]).filter(f=>f.includes("Test")||f.includes("Spec")||f.includes(".Tests/"));
  const allSrcFiles  = walkFiles(ROOT,[".cs"]).filter(f=>!f.includes("Test")&&!f.includes(".Tests/")&&!f.includes("obj/"));
  return {
    runtime:".NET", language:"C#", framework, packageManager:"NuGet / dotnet CLI",
    existing:{ testFramework:has("xunit","XUnit")?"xUnit":has("nunit","NUnit")?"NUnit":has("mstest","MSTest")?"MSTest":null,
      mockLib:has("Moq","moq")?"Moq":has("NSubstitute")?"NSubstitute":has("FakeItEasy")?"FakeItEasy":null,
      assertLib:has("FluentAssertions")?"FluentAssertions":has("Shouldly")?"Shouldly":null,
      e2eTool:has("Playwright")?"Playwright":null,
      configFiles:csproj.slice(0,4), testScript:"dotnet test" },
    meta:{ name:path.basename(ROOT), version:"0.0.0", hasCoverage:has("coverlet") },
    files:{ testExts:[".cs"], srcExts:[".cs"],
      testDirs:walkDirs(ROOT,2).filter(d=>d.includes("Test")||d.includes("Spec")),
      allTestFiles, allSrcFiles },
  };
}

function detectGo() {
  if (!exists("go.mod")) return null;
  const goMod = readRaw("go.mod");
  const has   = (...w) => w.some(x => goMod.includes(x));
  const framework = has("gin-gonic/gin")?"Gin":has("labstack/echo")?"Echo":has("go-chi/chi")?"Chi":has("gofiber/fiber")?"Fiber":"Go stdlib";
  const allTestFiles = walkFiles(ROOT,["_test.go"]);
  const allSrcFiles  = walkFiles(ROOT,[".go"]).filter(f=>!f.endsWith("_test.go"));
  return {
    runtime:"Go", language:"Go", framework, packageManager:"Go modules",
    existing:{ testFramework:has("stretchr/testify")?"testify":"stdlib testing",
      mockLib:has("uber.org/mock","golang/mock")?"gomock":has("testify/mock")?"testify/mock":null,
      assertLib:has("testify/assert")?"testify/assert":null, e2eTool:null,
      configFiles:["go.mod","go.sum"].filter(exists), testScript:"go test ./..." },
    meta:{ name:goMod.split("\n")[0].replace("module ","").trim(), version:"0.0.0" },
    files:{ testExts:["_test.go"], srcExts:[".go"], testDirs:[], allTestFiles, allSrcFiles },
  };
}

function detectRust() {
  if (!exists("Cargo.toml")) return null;
  const cargo = readRaw("Cargo.toml");
  const has   = (...w) => w.some(x => cargo.includes(x));
  return {
    runtime:"Rust", language:"Rust",
    framework:has("actix-web")?"Actix Web":has("axum")?"Axum":has("rocket")?"Rocket":"Rust stdlib",
    packageManager:"Cargo",
    existing:{ testFramework:"built-in #[test]", mockLib:has("mockall")?"mockall":null,
      assertLib:null, e2eTool:null, configFiles:["Cargo.toml"], testScript:"cargo test" },
    meta:{ name:path.basename(ROOT), version:"0.0.0" },
    files:{ testExts:[".rs"], srcExts:[".rs"], testDirs:["tests"].filter(exists),
      allTestFiles:walkFiles(ROOT,[".rs"]).filter(f=>f.startsWith("tests/")),
      allSrcFiles: walkFiles(ROOT,[".rs"]).filter(f=>f.startsWith("src/")&&!f.endsWith("_test.rs")) },
  };
}

function detectDeno() {
  if (!exists("deno.json")&&!exists("deno.jsonc")) return null;
  const cfg = readJSON("deno.json")||readJSON("deno.jsonc")||{};
  const allTestFiles = walkFiles(ROOT,[".test.ts",".test.js","_test.ts","_test.js"]);
  const allSrcFiles  = walkFiles(ROOT,[".ts",".js"]).filter(f=>!f.includes(".test.")&&!f.includes("_test."));
  return {
    runtime:"Deno", language:"TypeScript", framework:"Deno stdlib", packageManager:"Deno (jsr/npm)",
    existing:{ testFramework:"Deno.test (built-in)", mockLib:"@std/testing/mock", assertLib:"@std/assert",
      e2eTool:null, configFiles:["deno.json","deno.jsonc"].filter(exists), testScript:"deno test" },
    meta:{ name:cfg.name||path.basename(ROOT), version:cfg.version||"0.0.0" },
    files:{ testExts:[".test.ts","_test.ts"], srcExts:[".ts",".js"],
      testDirs:["tests","test"].filter(exists), allTestFiles, allSrcFiles },
  };
}

function detectPhp() {
  if (!exists("composer.json")) return null;
  const composer = readJSON("composer.json")||{};
  const all = JSON.stringify({...(composer.require||{}), ...(composer["require-dev"]||{})});
  const has = (...w) => w.some(x => all.includes(x));
  return {
    runtime:"PHP", language:"PHP",
    framework:has("laravel/framework")?"Laravel":has("symfony/")?"Symfony":has("slim/slim")?"Slim":"PHP",
    packageManager:"Composer",
    existing:{ testFramework:has("phpunit")?"PHPUnit":has("pestphp")?"Pest":null,
      mockLib:has("mockery")?"Mockery":null, assertLib:null, e2eTool:null,
      configFiles:["phpunit.xml","phpunit.xml.dist","composer.json"].filter(exists),
      testScript:"./vendor/bin/phpunit" },
    meta:{ name:composer.name||path.basename(ROOT), version:composer.version||"0.0.0" },
    files:{ testExts:["Test.php"], srcExts:[".php"], testDirs:["tests","test","Tests"].filter(exists),
      allTestFiles:walkFiles(ROOT,["Test.php"]),
      allSrcFiles: walkFiles(ROOT,[".php"]).filter(f=>!f.includes("Test")&&!f.includes("/test")) },
  };
}

function detectRuby() {
  if (!exists("Gemfile")) return null;
  const gem = readRaw("Gemfile");
  const has = (...w) => w.some(x => gem.includes(x));
  return {
    runtime:"Ruby", language:"Ruby",
    framework:has("rails")?"Ruby on Rails":has("sinatra")?"Sinatra":has("hanami")?"Hanami":"Ruby",
    packageManager:"Bundler",
    existing:{ testFramework:has("rspec")?"RSpec":has("minitest")?"Minitest":null,
      mockLib:has("factory_bot")?"FactoryBot":null, assertLib:null,
      e2eTool:has("capybara")?"Capybara":null,
      configFiles:[".rspec","spec/spec_helper.rb","spec/rails_helper.rb"].filter(exists),
      testScript:has("rspec")?"bundle exec rspec":"bundle exec rails test" },
    meta:{ name:path.basename(ROOT), version:"0.0.0" },
    files:{ testExts:["_spec.rb","_test.rb"], srcExts:[".rb"],
      testDirs:["spec","test"].filter(exists),
      allTestFiles:walkFiles(ROOT,["_spec.rb","_test.rb"]),
      allSrcFiles: walkFiles(ROOT,[".rb"]).filter(f=>!f.includes("spec/")&&!f.includes("test/")) },
  };
}

function detectProject() {
  return detectNode()||detectPython()||detectJava()||detectDotNet()||
         detectGo()||detectRust()||detectDeno()||detectPhp()||detectRuby()||null;
}

// CONTEXT BUILDER
function buildContext(det, existing=null) {
  const allDirs   = walkDirs(ROOT);
  const arch      = detectArchitecture(allDirs);
  const structure = mapFolderStructure(allDirs, det.files.allSrcFiles);
  const coverage  = analyzeCoverage(det.files.allSrcFiles, det.files.allTestFiles);
  const style     = learnTestStyle(det.files.allTestFiles);
  const now       = new Date().toISOString();

  const sortedUntested = [...coverage.untested].sort((a,b) => {
    const aH = arch.suggestedTestDirs.some(d=>a.startsWith(d));
    const bH = arch.suggestedTestDirs.some(d=>b.startsWith(d));
    if (aH&&!bH) return -1; if (!aH&&bH) return 1; return 0;
  });

  return {
    _meta:{
      version:"3.0.0",
      createdAt:existing?._meta?.createdAt||now,
      updatedAt:now,
      projectRoot:ROOT,
      projectName:det.meta.name,
      scanCommand:"node testcraft-init.js",
      refreshCommand:"node testcraft-init.js --refresh",
    },
    project:{
      name:det.meta.name, version:det.meta.version,
      runtime:det.runtime, language:det.language,
      framework:det.framework, packageManager:det.packageManager,
      hasTypeScript:det.meta.hasTypeScript||false,
    },
    architecture:{
      pattern:arch.pattern, layers:arch.layers,
      testStrategy:arch.testStrategy, suggestedTestDirs:arch.suggestedTestDirs,
      allDirectories:allDirs.slice(0,60), folderMap:structure,
    },
    detectedTools:{
      testFramework:det.existing.testFramework,
      mockLib:det.existing.mockLib,
      assertLib:det.existing.assertLib||null,
      e2eTool:det.existing.e2eTool,
      configFiles:det.existing.configFiles,
      testScript:det.existing.testScript,
      hasCoverage:det.meta.hasCoverage||false,
    },
    chosenTools: existing?.chosenTools||{
      testFramework:det.existing.testFramework||null,
      mockLib:det.existing.mockLib||null,
      assertLib:det.existing.assertLib||null,
      e2eTool:det.existing.e2eTool||null,
      confirmed:!!det.existing.testFramework,
      confirmedAt:det.existing.testFramework?now:null,
    },
    learnedStyle: style ? {
      fileNaming:style.fileNaming,
      coLocation:style.coLocation,
      separateTestDir:style.separateDir,
      describeExamples:style.describeStyle.slice(0,5),
      itExamples:style.itStyle.slice(0,6),
      assertionStyle:style.assertStyle,
      mockingStyle:style.mockStyle,
      factoryPatterns:style.factoryPatterns,
      usesAAAComments:style.aaaComments,
      importExamples:style.importPatterns.slice(0,8),
      codeSnippets:style.snippets.slice(0,3),
      totalTestsFound:det.files.allTestFiles.length,
    } : null,
    files:{
      testDirs:det.files.testDirs,
      srcFileCount:det.files.allSrcFiles.length,
      testFileCount:det.files.allTestFiles.length,
      allSrcFiles:det.files.allSrcFiles,
      allTestFiles:det.files.allTestFiles,
    },
    progress:{
      coveragePercent:coverage.coveragePercent,
      testedFiles:existing?.progress?.testedFiles||coverage.tested,
      untestedFiles:sortedUntested,
      totalSrcFiles:det.files.allSrcFiles.length,
      nextSuggested:sortedUntested.slice(0,5),
      lastTestedFile:existing?.progress?.lastTestedFile||null,
      lastTestedAt:existing?.progress?.lastTestedAt||null,
    },
    session:{
      currentFile:null, currentTestType:null,
      history:existing?.session?.history||[],
      notes:existing?.session?.notes||[],
    },
    preferences: existing?.preferences||{
      language:null, additionalContext:null,
    },
  };
}

// REPORT PRINTERS
function printReport(det, ctx) {
  log();
  log(`${C.bold}${C.cyan}╔════════════════════════════════════════════════════╗${C.reset}`);
  log(`${C.bold}${C.cyan}║        🧪  TestCraft — Project Scanner v3          ║${C.reset}`);
  log(`${C.bold}${C.cyan}╚════════════════════════════════════════════════════╝${C.reset}`);
  log();
  label("Project",det.meta.name);
  label("Root   ",ROOT);

  head("🔍  Stack Detected");
  label("Runtime   ",det.runtime);
  label("Language  ",det.language);
  label("Framework ",det.framework);
  label("Pkg Mgr   ",det.packageManager);
  if (det.meta.hasTypeScript) ok("TypeScript detected");

  head("🏗️  Architecture");
  label("Pattern  ",ctx.architecture.pattern);
  label("Strategy ",ctx.architecture.testStrategy);
  if (ctx.architecture.layers.length) label("Layers   ",ctx.architecture.layers.join(", "));
  log(`  ${C.dim}Directories found: ${ctx.architecture.allDirectories.slice(0,10).join(", ")}${C.reset}`);

  head("🧰  Test Infrastructure");
  if (det.existing.testFramework) ok(`Test framework : ${C.white}${det.existing.testFramework}${C.reset}`);
  else warn("No test framework found — TestCraft will help you choose one");
  if (det.existing.mockLib)   ok(`Mock library   : ${det.existing.mockLib}`);
  if (det.existing.assertLib) ok(`Assert library : ${det.existing.assertLib}`);
  if (det.existing.e2eTool)   ok(`E2E tool       : ${det.existing.e2eTool}`);
  if (det.existing.configFiles.length) ok(`Config files   : ${det.existing.configFiles.join(", ")}`);
  label("Run tests  ", det.existing.testScript||"(not detected)");

  head("📊  Coverage Snapshot");
  label("Source files",`${det.files.allSrcFiles.length} found`);
  label("Test files  ",`${det.files.allTestFiles.length} found`);
  label("Coverage est",`~${ctx.progress.coveragePercent}%`);
  if (ctx.progress.nextSuggested.length) {
    log(`  ${C.dim}Next files to test (priority order):${C.reset}`);
    ctx.progress.nextSuggested.forEach(f=>log(`    ${C.dim}→ ${f}${C.reset}`));
  }

  if (ctx.learnedStyle) {
    head("🎨  Learned Test Style (from your existing tests)");
    label("File naming  ",ctx.learnedStyle.fileNaming);
    label("Location     ",ctx.learnedStyle.coLocation?"co-located with source":"separate test directory");
    if (ctx.learnedStyle.assertionStyle.length) label("Assertions   ",ctx.learnedStyle.assertionStyle.join(", "));
    if (ctx.learnedStyle.mockingStyle.length)   label("Mocking      ",ctx.learnedStyle.mockingStyle.join(", "));
    if (ctx.learnedStyle.factoryPatterns.length) label("Data factories",ctx.learnedStyle.factoryPatterns.join(", "));
    if (ctx.learnedStyle.usesAAAComments) ok("Team uses // Arrange // Act // Assert comments");
    if (ctx.learnedStyle.itExamples.length) {
      log(`  ${C.dim}Naming style examples:${C.reset}`);
      ctx.learnedStyle.itExamples.slice(0,3).forEach(ex=>log(`    ${C.dim}${ex}${C.reset}`));
    }
  } else {
    log(`  ${C.dim}No existing tests found — style will be set by the team${C.reset}`);
  }

  head("🚀  You're ready. What to do next");
  info("Context saved  → .testcraft/context.json");
  info("Commit it to share test progress with your team");
  info("Open TestCraft chat and load the context.json");
  info(`Tell the agent: "test this file: ${ctx.progress.nextSuggested[0]||"src/..."}"  `);
  log();
  log(`  ${C.dim}To update after adding files:${C.reset}  ${C.white}node testcraft-init.js --refresh${C.reset}`);
  log(`  ${C.dim}To check progress anytime:${C.reset}   ${C.white}node testcraft-init.js --status${C.reset}`);
  log();
}

function printStatus(ctx) {
  const pct = ctx.progress.coveragePercent;
  const filled = Math.round(pct/5);
  const bar = `${"\u2588".repeat(filled)}${"\u2591".repeat(20-filled)}`;
  log();
  log(`${C.bold}${C.cyan}  🧪 TestCraft Status — ${ctx.project.name}${C.reset}`);
  hr();
  label("Stack   ",`${ctx.project.language} / ${ctx.project.framework}`);
  label("Tools   ",ctx.chosenTools.confirmed
    ? `${ctx.chosenTools.testFramework} (confirmed ✓)`
    : `${ctx.detectedTools.testFramework||"not set"} (not confirmed yet)`);
  label("Pattern ",ctx.architecture?.pattern||"unknown");
  log();
  log(`  Coverage  ${C.cyan}${bar}${C.reset}  ${C.bold}${pct}%${C.reset}`);
  label("Tested  ",`${ctx.progress.testedFiles.length} / ${ctx.progress.totalSrcFiles} files`);
  label("Untested",`${ctx.progress.untestedFiles.length} files`);
  if (ctx.progress.lastTestedFile) {
    label("Last    ",`${ctx.progress.lastTestedFile} (${ctx.progress.lastTestedAt||"unknown"})`);
  }
  if (ctx.session.history.length) {
    log();
    log(`  ${C.dim}Session history (last 5):${C.reset}`);
    ctx.session.history.slice(-5).reverse().forEach(h=>
      log(`  ${C.dim}  → ${h.file} [${h.testType}] — ${h.summary}${C.reset}`)
    );
  }
  if (ctx.learnedStyle) {
    log();
    log(`  ${C.dim}Learned style: ${ctx.learnedStyle.fileNaming} naming, ${ctx.learnedStyle.assertionStyle.join("+")||"unknown"} assertions${C.reset}`);
  }
  log();
}

function ensureGitignore() {
  const giPath = path.join(ROOT,".gitignore");
  const content = fs.existsSync(giPath) ? fs.readFileSync(giPath,"utf8") : "";
  if (!content.includes("testcraft")) {
    fs.appendFileSync(giPath,"\n# TestCraft — commit context.json to share progress with team\n# .testcraft/context.json\n");
    ok(".gitignore updated (context.json not ignored — commit it with your team)");
  }
}

// MAIN
const args      = process.argv.slice(2);
const isReset   = args.includes("--reset");
const isStatus  = args.includes("--status");
const isScan    = args.includes("--scan");
const isRefresh = args.includes("--refresh");

if (isReset) {
  if (fs.existsSync(CTX_FILE)) { fs.unlinkSync(CTX_FILE); ok("Context cleared."); }
  else warn("No context file found.");
  process.exit(0);
}

if (isStatus) {
  const ctx = readJSON(CTX_FILE);
  if (!ctx) { warn("No context found. Run: node testcraft-init.js"); process.exit(1); }
  printStatus(ctx); process.exit(0);
}

log();
info("Scanning project...");
const detected = detectProject();

if (!detected) {
  log();
  warn("Could not detect a supported project here.");
  warn("Supported: Node.js · Python · Java/Kotlin · .NET · Go · Rust · Deno · PHP · Ruby");
  warn("Run from your project root directory.");
  log(); process.exit(1);
}

const existingCtx = isRefresh ? readJSON(CTX_FILE) : null;
const ctx = buildContext(detected, existingCtx);
printReport(detected, ctx);

if (!isScan) {
  if (!fs.existsSync(TC_DIR)) fs.mkdirSync(TC_DIR,{ recursive:true });
  fs.writeFileSync(CTX_FILE, JSON.stringify(ctx,null,2),"utf8");
  ensureGitignore();
  ok(`Context saved → .testcraft/context.json`);
  if (isRefresh) info("Progress and preferences preserved from previous scan.");
  log();
}
