set background=dark
set number
set relativenumber
set mouse=a
set tabstop=4
set expandtab
set showcmd
set cursorline
set t_Co=256
set termguicolors
highlight CursorLine cterm=NONE ctermbg=237 guibg=#3a3a3a
set wildmenu
set showmatch
set incsearch
set clipboard=unnamed
syntax enable
set scrolloff=8
set smartcase
set hlsearch
set splitright
set smartindent
set undodir=~/.vim/undodir
set undofile
set undolevels=1000
let &t_SI = "\e[6 q"        " thin line in insert mode
let &t_EI = "\e[2 q"        " block in normal mode
let g:cheatsheet_open = 0
let mapleader = " "
function! ToggleCheatsheet()
    if g:cheatsheet_open
        bdelete cheatsheet.txt
        let g:cheatsheet_open = 0
    else
        vsplit ~/.vim/cheatsheet.txt
        vertical resize 50
        setlocal nomodifiable buftype=nofile
        wincmd p
        let g:cheatsheet_open = 1
    endif
endfunction
nnoremap <leader>? :call ToggleCheatsheet()<CR>
autocmd QuitPre * silent! bdelete cheatsheet.txt
autocmd WinEnter * if winnr('$') == 1 && bufname('%') == 'cheatsheet.txt' | q | endif
