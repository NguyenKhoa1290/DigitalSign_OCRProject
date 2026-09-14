import sys
sys.stdout.reconfigure(encoding='utf-8')
import docx

doc = docx.Document('Do an tot nghiep.docx')

tables_info = []
for i, table in enumerate(doc.tables):
    rows_data = []
    for row in table.rows:
        row_data = [cell.text.strip() for cell in row.cells]
        rows_data.append(row_data)
    tables_info.append({'table_index': i, 'rows': rows_data})

with open('tables_content.txt', 'w', encoding='utf-8') as f:
    for t in tables_info:
        idx = t['table_index']
        f.write(f'=== TABLE {idx} ===\n')
        for row in t['rows']:
            f.write(' | '.join(row) + '\n')
        f.write('\n')

print(f'Total tables: {len(tables_info)}')
