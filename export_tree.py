import os
from pathlib import Path


def export_tree(directory=".", output_file="目录结构.txt", indent_str="    ", include_files=True):
    """
    导出目录结构到文本文件
    
    参数:
        directory: 要扫描的起始目录，默认为当前目录
        output_file: 输出的txt文件名
        indent_str: 缩进字符串，默认为4个空格
        include_files: 是否包含文件（True则包含文件和文件夹，False则仅文件夹）
    """
    directory = Path(directory).resolve()
    
    if not directory.exists():
        print(f"错误: 目录 '{directory}' 不存在")
        return
    
    lines = []
    lines.append(f" 目录结构: {directory}")
    lines.append("=" * 50)
    lines.append("")
    
    def walk_dir(current_path, level=0):
        """递归遍历目录"""
        try:
            # 获取当前目录下的所有项目并排序（文件夹在前，文件在后）
            items = list(current_path.iterdir())
            
            # 分离文件夹和文件
            dirs = sorted([item for item in items if item.is_dir()], key=lambda x: x.name)
            files = sorted([item for item in items if item.is_file()], key=lambda x: x.name) if include_files else []
            
            # 处理文件夹
            for idx, item in enumerate(dirs):
                is_last = (idx == len(dirs) - 1 and len(files) == 0)
                prefix = "└── " if is_last else "├── "
                lines.append(f"{indent_str * level}{prefix} {item.name}/")
                walk_dir(item, level + 1)
            
            # 处理文件
            for idx, item in enumerate(files):
                is_last = (idx == len(files) - 1)
                prefix = "└── " if is_last else "├── "
                ext = item.suffix.lower()
             
                
                lines.append(f"{indent_str * level}{prefix} {item.name}")
                
        except PermissionError:
            lines.append(f"{indent_str * level} [权限不足，无法访问]")
        except Exception as e:
            lines.append(f"{indent_str * level} [错误: {str(e)}]")
    
    # 开始遍历
    walk_dir(directory)
    
    # 添加统计信息
    lines.append("")
    lines.append("=" * 50)
    lines.append(f" 总计: {len(list(directory.rglob('*')))} 个项目")
    lines.append(f" 导出时间: {os.popen('date /t && time /t').read().strip() if os.name == 'nt' else os.popen('date').read().strip()}")
    
    # 写入文件（使用UTF-8编码确保中文正常显示）
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    
    print(f" 导出完成！文件保存在: {output_file}")
    print(f" 根目录: {directory}")


# ===== 使用示例 =====
if __name__ == "__main__":
    # 示例1: 导出当前目录（包含文件）
    export_tree(".", "我的文件夹结构.txt")
    
    # 示例2: 仅导出文件夹结构（不含文件）
    export_tree(".", "仅文件夹结构.txt", include_files=False)
    
    # 示例3: 导出指定路径，使用2空格缩进
    # export_tree("C:/Users/YourName/Documents", "文档结构.txt", indent_str="  ")