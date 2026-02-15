const PORT = 30078;

// DOM 要素
const filenameInput = document.getElementById('filename');
const titleInput = document.getElementById('title');
const authorInput = document.getElementById('author');
const previewTitle = document.getElementById('preview-title');
const previewAuthor = document.getElementById('preview-author');
const preview = document.getElementById('preview');
const saveBtn = document.getElementById('saveBtn');
const deleteBtn = document.getElementById('deleteBtn');
const statusSpan = document.getElementById('status');

let editor = null;
let debounceTimer = null;
const debounce = (fn, ms = 300) => {
    return (...args) => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => fn(...args), ms);
    };
};

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

// 前回の状態を保持して差分判定するオブジェクト
const prevState = {
    title: null,
    author: null,
    md: null,
    html: null,
};

class Editor
{
    static editor;
}

const initEditor = async () => {
    require.config({ paths: { 'vs': 'https://microsoft.github.io/monaco-editor/node_modules/monaco-editor/dev/vs/' } });

    require(["vs/editor/editor.main"], () => {
        Editor.editor = monaco.editor.create(document.getElementById('editor'), {
            value: "",
            language: 'markdown',
            theme: 'vs',
            automaticLayout: true,
            minimap: { enabled: false },
            wordWrap: 'on',
            lineNumbers: 'on',
            fontSize: 14,
        });
        // 初期プレビュー描画
        updatePreview();
        Editor.editor.onDidChangeModelContent(debounce(() => {
            updatePreview();
        }, 100));

        console.log(Editor.editor);
    });

    // イベント登録（プレビュー更新、保存）
    titleInput.addEventListener('input', debounce(updatePreview, 150));
    authorInput.addEventListener('input', debounce(updatePreview, 150));
    saveBtn.addEventListener('click', onSave);
}

deleteBtn.addEventListener('click', async () => {
    const fileNameRaw = filenameInput.value.trim();
    const content = getContent();

    if (!fileNameRaw) {
        statusSpan.textContent = "ファイル名を入力してください。";
        return;
    }

    saveBtn.disabled = true;
    statusSpan.textContent = "削除中...";

    const payload = {
        fileName: fileNameRaw
    };

    try {
        const res = await fetch(`http://localhost:${PORT}/delete`, {
            method: 'DELETE',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });

        if (!res.ok) {
            const txt = await res.text();
            statusSpan.textContent = "削除に失敗しました: " + (txt || res.statusText);
        } else {
            const json = await res.json();
            statusSpan.textContent = "削除しました: " + (json.path || 'Saved');
        }
    } catch (err) {
        statusSpan.textContent = "削除に失敗しました（サーバー接続エラー）";
        console.error(err);
    } finally {
        saveBtn.disabled = false;
        setTimeout(() => { statusSpan.textContent = ""; }, 4000);
    }
});

function getContent() {
    return Editor.editor ? Editor.editor.getValue() : '';
}

function stripFrontMatter(markdown) {
  // 先頭にある --- から次の --- まで（改行を含む任意の内容）を取り除く
  return markdown.replace(/^\s*---\r?\n[\s\S]*?\r?\n---\r?\n?/, '');
}

function updatePreview() {
    const title = titleInput.value.trim();
    const author = authorInput.value.trim();
    let md = getContent();
    
    const rewriteImages = (markdown) => {
        const BASE = `http://localhost:${PORT}/image`;
        return markdown.replace(
            /!\[([^\]]*)\]\((\/[^)\s]+)(?:\s+"[^"]*")?\)/g,
            (match, alt, path) => {
                // query に入れるときはエンコード（スラッシュは元のままにする）
                const encodedPath = encodeURIComponent(path).replace(/%2F/g, '/');
                return `![${alt}](${BASE}?path=${encodedPath})`;
            }
        );
    }

    md = rewriteImages(md);

    // タイトルが変わっていれば更新
    if (title !== prevState.title) {
        previewTitle.textContent = title || 'タイトルがここに表示されます';
        prevState.title = title;
    }

    // 著者が変わっていれば更新
    if (author !== prevState.author) {
        previewAuthor.textContent = author ? `by ${author}` : '';
        prevState.author = author;
    }
    
    // Markdown 本文が変わっていれば HTML を再生成して更新
    if (md !== prevState.md) {
        try {
            const html = marked.parse(md);

            // 生成 HTML が前回と違う場合のみ DOM を更新
            if (html !== prevState.html) {
                preview.innerHTML = html;
                prevState.html = html;
            }
        } catch (err) {
            preview.innerHTML = '<p style="color:red;">プレビューの生成に失敗しました。</p>';
            console.error(err);
            // 解析に失敗しても md を prevState に記録しておくことで
            // 同じ入力に対して無駄に再解析することを防ぐ（必要ならこの動作は変更可）
        }
        // 前回の md を更新（解析成功/失敗にかかわらず）
        prevState.md = md;
    }
}

initEditor();

async function onSave() {
    const fileNameRaw = filenameInput.value.trim();
    const title = titleInput.value.trim();
    const author = authorInput.value.trim();
    const content = getContent();

    if (!fileNameRaw) {
        statusSpan.textContent = "ファイル名を入力してください。";
        return;
    }

    saveBtn.disabled = true;
    statusSpan.textContent = "保存中...";

    const payload = {
        fileName: fileNameRaw,
        title,
        author,
        content
    };

    try {
        const res = await fetch(`http://localhost:${PORT}/save`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });

        if (!res.ok) {
            const txt = await res.text();
            statusSpan.textContent = "保存に失敗しました: " + (txt || res.statusText);
        } else {
            const json = await res.json();
            statusSpan.textContent = "保存しました: " + (json.path || 'Saved');
        }
    } catch (err) {
        statusSpan.textContent = "保存に失敗しました（サーバー接続エラー）";
        console.error(err);
    } finally {
        saveBtn.disabled = false;
        setTimeout(() => { statusSpan.textContent = ""; }, 4000);
    }
}

const LIST_URL = `http://localhost:${PORT}/list`; // ←ファイル一覧を返すエンドポイント
const FILE_FETCH_URL = `http://localhost:${PORT}/file?path=`; // ←ファイルコンテンツを返すエンドポイント（?path=relativePath）

const fileListEl = document.getElementById('fileList');

// ファイル一覧を取得して描画する
async function fetchFileList() {
    try {
        const res = await fetch(LIST_URL);
        if (!res.ok) throw new Error('failed to fetch file list');
        const list = await res.json();
        return renderFileList(list || []);
    } catch (err) {
        console.error('fetchFileList error:', err);
        fileListEl.innerHTML = '<div style="color:tomato;padding:8px;">ファイル一覧の取得に失敗しました。</div>';
    }
}

function renderFileList(list) {
    fileListEl.innerHTML = ''; // クリア
    if (!list.length) {
        fileListEl.innerHTML = '<div style="color:var(--muted);padding:8px;">ファイルがありません。</div>';
        return;
    }

    for(const item of list){
        const row = document.createElement('div');
        row.className = 'file-item';
        row.dataset.relativePath = item.relativePath;
        row.dataset.name = item.name;
    
        const left = document.createElement('div');
        left.classList.add("file-item-name");
        left.style.flex = '1';
    
        const nameEl = document.createElement('div');
        nameEl.className = 'name';
        nameEl.classList.add("file-item-path");
        nameEl.textContent = item.relativePath;
    
        left.appendChild(nameEl);    
        row.appendChild(left);
    
        // クリックでファイルを読み込む
        row.addEventListener('click', async () => {
            // 選択表示
            document.querySelectorAll('.file-item').forEach(el => el.classList.remove('active'));
            row.classList.add('active');
    
            await loadFileToEditor(item);
        });
    
        fileListEl.insertBefore(row, fileListEl.firstChild);
    }
}

// サーバーからファイル本文を取ってきて、タイトル（YAML / H1）を抽出する
async function fetchTitleForListItem(item) {
    try {
        // const content = await fetchFileContent(item.relativePath);
        // const parsed = parseFrontmatterOrHeading(content);
        // console.log(item)
        return item.title || '';
    } catch (err) {
        console.error('fetchTitleForListItem error', err);
        return '';
    }
}

async function fetchFileContent(filePath) {
    const url = FILE_FETCH_URL + encodeURIComponent(filePath);
    const res = await fetch(url);
    if (!res.ok) throw new Error('failed to fetch file content');
    return JSON.parse(await res.text());
}

// Markdown からタイトル/author を抽出（YAML frontmatter の title/author を優先し、なければ最初の H1 を title とする）
function parseFrontmatterOrHeading(mdText) {
    const result = { title: '', author: '' };

    // frontmatter を探す
    const fmMatch = mdText.match(/^---\s*([\s\S]*?)\s*---/);
    if (fmMatch) {
        const fm = fmMatch[1];
        // title: ... の行を探す（簡易パース）
        const tMatch = fm.match(/^\s*title\s*:\s*["']?(.+?)["']?\s*$/im);
        if (tMatch) result.title = tMatch[1].trim();
        const aMatch = fm.match(/^\s*authors\s*:\s*["']?(.+?)["']?\s*$/im);
        if (aMatch) result.author = aMatch[1].trim();
        if (result.title || result.author) return result;
    }

    // YAML がなければ最初の H1 を探す
    const h1Match = mdText.match(/^\s*#\s+(.+)$/m);
    if (h1Match) {
        result.title = h1Match[1].trim();
    }

    return result;
}

// ファイルをエディタにロードする（filename/title/author も埋める）
async function loadFileToEditor(item) {
    try {
        saveBtn.disabled = true;
        statusSpan.textContent = '読み込み中...';
        const content = await fetchFileContent(item.fullPath);

        // ファイル名（拡張子なし）を filename input に入れる
        filenameInput.value = item.name;

        // parse title/author
        const parsed = parseFrontmatterOrHeading(content.content);
        if (parsed.title) titleInput.value = parsed.title;
        else titleInput.value = ''; // 既存の値をクリアしたければ

        if (parsed.author) authorInput.value = parsed.author;
        // editor に読み込む
        if (Editor.editor) {
            console.log(content)
            const md = stripFrontMatter(content.content);
            Editor.editor.setValue(md);
            // prevState をクリアして必ず再描画させたい場合は:
            prevState.md = null;
            prevState.html = null;
            updatePreview();
        }

        statusSpan.textContent = '読み込み完了';
    } catch (err) {
        console.error('loadFileToEditor error', err);
        statusSpan.textContent = 'ファイルの読み込みに失敗しました';
    } finally {
        saveBtn.disabled = false;
        setTimeout(()=> { statusSpan.textContent = ''; }, 2500);
    }
}

window.addEventListener('dragover', (e) => {
    e.preventDefault();
}, false);

window.addEventListener('drop', (e) => {
    e.preventDefault(); // ドロップの挙動を抑制
    // ここでファイルを受け取る処理を行う
    console.log("File dropped, but default action prevented.");
}, false);

const uploadFile = async (fileData) => {
    try {
        const fileDataObj = JSON.parse(fileData);
        fileDataObj["blogFileName"] = document.getElementById("filename").value;
        const res = await fetch(`http://localhost:${PORT}/upload`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(fileDataObj),
        });

        if (!res.ok) {
            const txt = await res.text();
            statusSpan.textContent = "保存に失敗しました: " + (txt || res.statusText);
        } else {
            const json = await res.json();
            statusSpan.textContent = "保存しました: " + (json.path || 'Saved');

            Editor.editor.executeEdits('', [{
                range: Editor.editor.getSelection(),
                text: `![](/img/blog/${fileDataObj.blogFileName}/${fileDataObj.fileName})\n`,
            }])
        }
    } catch (err) {
        statusSpan.textContent = "保存に失敗しました（サーバー接続エラー）";
        console.error(err);
    } finally {
        saveBtn.disabled = false;
        setTimeout(() => { statusSpan.textContent = ""; }, 4000);
    }
}

document.getElementById("newBtn").addEventListener("click", () => {
    const d = new Date();
    const date = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}-`;
    document.getElementById("filename").value = date;
    document.getElementById("title").value = "";
    document.getElementById("author").value = "";
    Editor.editor.setValue("## 見出し\n\n本文");
})

// ファイル一覧を読み込む
fetchFileList();
